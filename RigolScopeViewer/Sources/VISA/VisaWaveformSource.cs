using Microsoft.Extensions.Logging;
using RigolScopeViewer.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RigolScopeViewer.Models;
using System.Linq;
using CommunityToolkit.Mvvm.Input;

namespace RigolScopeViewer.Sources.VISA;

public class VisaWaveformSource : IWaveformSource
{
    private readonly IConfigManager _configManager;
    private readonly ILogger<VisaWaveformSource> _logger;
    private readonly IAlertModal _alertModal;
    private VisaConfig _config;

    private float[][]? _channelData;
    private WaveformMetadata[]? _metadata;
    private int _channelCount = 0;

    // Постійний клієнт для режиму Live View (щоб не переповнювати LXI TIME_WAIT)
    private ScpiClient? _persistentClient;

    public event EventHandler? DataReady;

    public int ChannelCount => _channelCount;

    public object? ControlPanelViewModel { get; private set; }

    public VisaWaveformSource(IConfigManager configManager, ILogger<VisaWaveformSource> logger, IAlertModal alertModal)
    {
        _configManager = configManager;
        _logger = logger;
        _alertModal = alertModal;
        _config = _configManager.Load<VisaConfig>("visa_config.json") ?? new VisaConfig();

        // Віддаємо себе у ViewModel панелі керування
        ControlPanelViewModel = new VisaControlViewModel(this);
    }

    // --- КЕРУВАННЯ З'ЄДНАННЯМ ДЛЯ LIVE VIEW ---

    public void ConnectPersistent()
    {
        if (_persistentClient != null) return;
        _persistentClient = new ScpiClient(_config.IpAddress, _config.Port, _config.TimeoutMs);
        _logger.LogInformation("Persistent LXI connection established.");
    }

    public void DisconnectPersistent()
    {
        if (_persistentClient != null)
        {
            _persistentClient.Dispose();
            _persistentClient = null;
            _logger.LogInformation("Persistent LXI connection closed.");
        }
    }

    public async Task ResetLxiAsync()
    {
        _logger.LogWarning("Hard resetting LXI connection...");

        DisconnectPersistent();

        // Даємо процесору осцилографа час звільнити сокети ОС (критично важливо!)
        await Task.Delay(1000);

        ConnectPersistent();

        if (_persistentClient != null)
        {
            // Clear Status: скидає внутрішні помилки та регістри
            await _persistentClient.WriteAsync("*CLS");
        }

        _logger.LogInformation("LXI connection reset successfully.");
    }

    // --- SETUP ТА ТЕСТУВАННЯ ---

    public async Task TestConnectionAsync()
    {
        try
        {
            using var client = new ScpiClient(_config.IpAddress, _config.Port, _config.TimeoutMs);
            var idn = await client.QueryStringAsync("*IDN?");
            _alertModal.Show("Connection Successful", $"Successfully connected \n to VISA device:\n\n{idn.Trim()}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VISA connection test failed.");
            _alertModal.Show("Connection Failed", $"Failed to connect to VISA device. \nPlease check the IP address, port, and network connection.\n\nError details: {ex.Message}");
        }
    }

    public async Task<bool> RunSetupAsync()
    {
        var appLifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        var mainWindow = appLifetime?.MainWindow;

        var scanner = new ZeroconfScanner();
        var vm = new RigolScopeViewer.ViewModels.SetupWizardViewModel
        {
            ConfigObject = _config,
            CurrentFilePath = "VISA (LXI SCPI over TCP)"
        };

        vm.PreviewContent = PreviewerFactory.CreateZeroconfPreviewer(scanner, (ip) =>
        {
            vm.Properties.FirstOrDefault(p => p.Name == nameof(VisaConfig.IpAddress))?.Value = ip;
            _logger.LogInformation("Selected VISA device at IP: {IpAddress}", ip);
        });

        var dialog = new RigolScopeViewer.Views.SetupWizardWindow
        {
            DataContext = vm
        };
        dialog.TestCommand = new RelayCommand(async () => await TestConnectionAsync());

        var result = false;
        if (mainWindow != null)
        {
            result = await dialog.ShowDialog<bool>(mainWindow);
        }
        else
        {
            dialog.Show();
            result = true;
        }

        if (!result) return false;

        _configManager.Save(_config, "visa_config.json");
        _logger.LogInformation("VisaWaveformSource setup for {IpAddress}:{Port}", _config.IpAddress, _config.Port);

        return await CaptureAsync();
    }

    // --- ЛОГІКА ЗЧИТУВАННЯ ДАНИХ ---

    // Одноразове зчитування (для першого кадру після Setup)
    public async Task<bool> CaptureAsync()
    {
        try
        {
            using var client = new ScpiClient(_config.IpAddress, _config.Port, _config.TimeoutMs);
            return await FetchDataFromScopeAsync(client, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture from VISA.");
            _alertModal.Show("VISA Capture Error", $"Failed to capture waveform data from the VISA device.\n\nError details: {ex.Message}");
            return false;
        }
    }

    // Циклічне зчитування (Викликається з VisaControlViewModel під час Live View)
    internal async Task FetchDataAsync(CancellationToken ct)
    {
        if (_persistentClient == null)
            throw new InvalidOperationException("Persistent client is not connected.");

        await FetchDataFromScopeAsync(_persistentClient, ct);

        // Кажемо головному вікну малювати!
        DataReady?.Invoke(this, EventArgs.Empty);
    }

    // Загальне ядро для стягування SCPI даних
    private async Task<bool> FetchDataFromScopeAsync(ScpiClient client, CancellationToken ct)
    {
        var idn = await client.QueryStringAsync("*IDN?", ct);
        _logger.LogDebug("Connected to: {Idn}", idn.Trim());

        var tempChannels = new List<float[]>();
        var tempMetadata = new List<WaveformMetadata>();

        for (var ch = 1; ch <= 4; ch++)
        {
            ct.ThrowIfCancellationRequested();

            var disp = await client.QueryStringAsync($":CHANnel{ch}:DISPlay?", ct);
            if (disp.Trim() != "1") continue;

            await client.WriteAsync($":WAVeform:SOURce CHANnel{ch}", ct);
            await client.WriteAsync(":WAVeform:FORMat BYTE", ct);
            await client.WriteAsync(":WAVeform:MODE NORMal", ct);

            var preambleStr = await client.QueryStringAsync(":WAVeform:PREamble?", ct);
            var vals = preambleStr.Split(',');
            if (vals.Length < 10) continue;

            var points = int.Parse(vals[2]);
            var xincrement = float.Parse(vals[4], System.Globalization.CultureInfo.InvariantCulture);
            var xorigin = float.Parse(vals[5], System.Globalization.CultureInfo.InvariantCulture);
            var xreference = float.Parse(vals[6], System.Globalization.CultureInfo.InvariantCulture);
            var yincrement = float.Parse(vals[7], System.Globalization.CultureInfo.InvariantCulture);
            var yorigin = float.Parse(vals[8], System.Globalization.CultureInfo.InvariantCulture);
            var yreference = float.Parse(vals[9], System.Globalization.CultureInfo.InvariantCulture);

            var rawData = await client.QueryBinaryValuesAsync(":WAVeform:DATA?", ct);

            var voltage = new float[rawData.Length];
            for (var i = 0; i < rawData.Length; i++)
            {
                voltage[i] = (rawData[i] - yreference) * yincrement + yorigin;
            }

            tempChannels.Add(voltage);
            tempMetadata.Add(new WaveformMetadata
            {
                ChannelName = $"CH{ch}",
                SampleInterval = xincrement,
                StartTime = xorigin - (xreference * xincrement),
                TotalPoints = voltage.Length
            });
        }

        _channelData = tempChannels.ToArray();
        _metadata = tempMetadata.ToArray();
        _channelCount = _channelData.Length;

        return _channelCount > 0;
    }

    // --- IWaveformSource ІМПЛЕМЕНТАЦІЯ ---

    public WaveformMetadata GetMetadata(int channelIndex)
    {
        if (_metadata == null || channelIndex < 0 || channelIndex >= _channelCount)
            throw new ArgumentOutOfRangeException(nameof(channelIndex));
        return _metadata[channelIndex];
    }

    public void ProcessChannelData(int channelIndex, TimeRange timeRange, DataProcessor processor, CancellationToken cancellationToken = default)
    {
        if (_channelData == null || channelIndex < 0 || channelIndex >= _channelCount) return;

        var meta = _metadata![channelIndex];
        var data = _channelData[channelIndex];

        // Zero-copy передача масиву в процесор
        ReadOnlySpan<float> slice = data.AsSpan();
        processor(slice, meta, cancellationToken);
    }

    public void Start() { /* Залишаємо пустим, бо ViewModel керує циклом */ }

    public void Stop() { /* Залишаємо пустим, бо ViewModel керує циклом */ }

    public void Dispose()
    {
        DisconnectPersistent(); // Обов'язково закриваємо сокет при знищенні!
        GC.SuppressFinalize(this);
        _channelData = null;
        _metadata = null;
    }
}
