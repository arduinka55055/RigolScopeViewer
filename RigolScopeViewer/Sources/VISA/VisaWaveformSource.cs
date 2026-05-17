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

    public event EventHandler? DataReady;

    public int ChannelCount => _channelCount;

    public VisaWaveformSource(IConfigManager configManager, ILogger<VisaWaveformSource> logger, IAlertModal alertModal)
    {
        _configManager = configManager;
        _logger = logger;
        _alertModal = alertModal;
        _config = _configManager.Load<VisaConfig>("visa_config.json") ?? new VisaConfig();
    }

    // null string: error
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

        try
        {
            using var client = new ScpiClient(_config.IpAddress, _config.Port, _config.TimeoutMs);

            var idn = await client.QueryStringAsync("*IDN?");
            _logger.LogInformation("Connected to: {Idn}", idn.Trim());

            var tempChannels = new List<float[]>();
            var tempMetadata = new List<WaveformMetadata>();

            for (var ch = 1; ch <= 4; ch++)
            {
                var disp = await client.QueryStringAsync($":CHANnel{ch}:DISPlay?");
                if (disp.Trim() != "1") continue;

                _logger?.LogInformation("Fetching waveform for CH{Ch}...", ch);
                await client.WriteAsync($":WAVeform:SOURce CHANnel{ch}");
                await client.WriteAsync(":WAVeform:FORMat BYTE");
                await client.WriteAsync(":WAVeform:MODE NORMal");

                var preambleStr = await client.QueryStringAsync(":WAVeform:PREamble?");
                var vals = preambleStr.Split(',');
                if (vals.Length < 10) continue;

                var points = int.Parse(vals[2]);
                var xincrement = float.Parse(vals[4], System.Globalization.CultureInfo.InvariantCulture);
                var xorigin = float.Parse(vals[5], System.Globalization.CultureInfo.InvariantCulture);
                var xreference = float.Parse(vals[6], System.Globalization.CultureInfo.InvariantCulture);
                var yincrement = float.Parse(vals[7], System.Globalization.CultureInfo.InvariantCulture);
                var yorigin = float.Parse(vals[8], System.Globalization.CultureInfo.InvariantCulture);
                var yreference = float.Parse(vals[9], System.Globalization.CultureInfo.InvariantCulture);

                var rawData = await client.QueryBinaryValuesAsync(":WAVeform:DATA?");

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture from VISA.");
            _alertModal.Show("VISA Capture Error", $"Failed to capture waveform data from the VISA device. Please check the connection and settings.\n\nError details: {ex.Message}");
            return false;
        }
    }

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

        var startIndex = (int)((timeRange.Start - meta.StartTime) / meta.SampleInterval);
        var endIndex = (int)((timeRange.End - meta.StartTime) / meta.SampleInterval);

        startIndex = Math.Clamp(startIndex, 0, data.Length);
        endIndex = Math.Clamp(endIndex, startIndex, data.Length);

        ReadOnlySpan<float> slice = data.AsSpan();
        processor(slice, meta, cancellationToken);
    }

    public void Start()
    {
        if (_channelData != null)
        {
            DataReady?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Stop() { }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _channelData = null;
        _metadata = null;
    }
}
