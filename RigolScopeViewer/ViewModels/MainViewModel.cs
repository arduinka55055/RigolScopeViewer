using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using RigolScopeViewer.Models;
using RigolScopeViewer.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using System.IO;
using System.Linq;
using System.Windows.Input;
using RigolScopeViewer.Sources.CSV;
using RigolScopeViewer.Sources;
using RigolScopeViewer.Interfaces;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;
using System.Threading;
using RigolScopeViewer.Sources.VISA;
using Avalonia.Threading; // Додано для безпечного виклику UI з бекграунду

namespace RigolScopeViewer.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private double _timePerDivision = 0.001; // 1ms/div

    [ObservableProperty]
    private double _timeOffset;

    [ObservableProperty]
    private int _screenWidthPx = 800; // Default fallback width

    private readonly ILogger<MainViewModel> _logger;
    private readonly IConfigManager _configManager;
    private readonly ILoggerFactory _loggerFactory;

    private readonly IOscilloscopePipeline _pipeline;
    private IWaveformSource? _currentLoader;
    private CancellationTokenSource? _renderCts;

    [ObservableProperty]
    private bool _isBusy; // Прив'яжи це до <ProgressBar IsIndeterminate="True" IsVisible="{Binding IsBusy}" />

    [ObservableProperty]
    private RenderFrame? _currentFrame; // Дані для нашого рендера

    // Саме цю властивість ти забіндиш в XAML до ContentControl, щоб показувати кнопки плагіна!
    [ObservableProperty]
    private IWaveformSource? _waveformSource;

    private readonly IEnumerable<IWaveformSourceFactory> _sourceFactories;

    public ObservableCollection<ChannelViewModel> Channels { get; } = [];
    public ICommand OpenCommand { get; }
    public ICommand CaptureCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }

    private bool _isUpdatingViewport = false;

    partial void OnTimePerDivisionChanged(double value)
    {
        if (!_isUpdatingViewport) _ = RequestNewFrameAsync();
    }

    partial void OnTimeOffsetChanged(double value)
    {
        if (!_isUpdatingViewport) _ = RequestNewFrameAsync();
    }

    private Color GetChannelColor(int index)
    {
        var colors = new[] { Colors.Yellow, Colors.Cyan, Colors.Magenta, Colors.Lime };
        return colors[index % colors.Length];
    }

    /// <summary>
    /// Constructor for dependency injection.
    /// </summary>
    public MainViewModel(
        ILogger<MainViewModel> logger,
        IConfigManager configManager,
        ILoggerFactory loggerFactory,
        IOscilloscopePipeline pipeline,
        IEnumerable<IWaveformSourceFactory> sourceFactories)
    {
        _logger = logger;
        _configManager = configManager;
        _pipeline = pipeline;
        _loggerFactory = loggerFactory;
        _sourceFactories = sourceFactories;

        _logger.LogInformation("MainViewModel initialized");

        OpenCommand = new RelayCommand(OpenFile);
        CaptureCommand = new RelayCommand(Capture);
        ZoomInCommand = new RelayCommand(() => TimePerDivision *= 0.8);
        ZoomOutCommand = new RelayCommand(() => TimePerDivision *= 1.25);
    }

    // --- КЕРУВАННЯ ПЛАГІНАМИ (Підписка на DataReady) ---

    private void SetCurrentLoader(IWaveformSource? newLoader)
    {
        // 1. Обов'язково відписуємось від старого лоадера, щоб збирач сміття (GC) міг його видалити
        if (_currentLoader != null)
        {
            _currentLoader.DataReady -= OnLoaderDataReady;
            _currentLoader.Dispose();
        }

        // 2. Встановлюємо новий
        _currentLoader = newLoader;
        WaveformSource = _currentLoader; // Оновлюємо UI (покаже/сховає ControlPanelViewModel)

        // 3. Підписуємось на події нового лоадера
        if (_currentLoader != null)
        {
            _currentLoader.DataReady += OnLoaderDataReady;
        }
    }

    private void OnLoaderDataReady(object? sender, EventArgs e)
    {
        // Цей метод викликається з ФОНОВОГО потоку (з циклу VISA).
        // Оскільки RequestNewFrameAsync змінює ObservableCollection та IsBusy (які прив'язані до UI),
        // ми зобов'язані перекинути виконання в UI-потік через Dispatcher!
        Dispatcher.UIThread.Post(() =>
        {
            _ = RequestNewFrameAsync();
        });
    }

    // ---------------------------------------------------

    [RelayCommand]
    private void ChangeViewport(ViewportChangeParams args)
    {
        _isUpdatingViewport = true;
        try
        {
            if (args.ZoomFactor != 1.0)
            {
                TimePerDivision *= args.ZoomFactor;
                if (TimePerDivision > 10000.0) TimePerDivision = 10000.0;
                if (TimePerDivision < 1e-9) TimePerDivision = 1e-9;
            }

            if (args.PanPercent != 0.0)
            {
                double totalScreenTime = TimePerDivision * 10.0;
                TimeOffset += totalScreenTime * args.PanPercent;

                if (TimeOffset > 100000.0) TimeOffset = 100000.0;
                if (TimeOffset < -100000.0) TimeOffset = -100000.0;
            }

            if (args.ScreenWidthPx > 0)
            {
                ScreenWidthPx = args.ScreenWidthPx;
            }
        }
        finally
        {
            _isUpdatingViewport = false;
        }

        _ = RequestNewFrameAsync();
    }

    private async void OpenFile()
    {
        _logger?.LogInformation("OpenFile dialog initiated");

        var appLifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        var mainWindow = appLifetime?.MainWindow;
        if (mainWindow == null) return;

        var fileFactories = _sourceFactories.Where(f => !f.IsCaptureSource).ToList();
        var extensions = fileFactories.SelectMany(f => f.SupportedExtensions).Select(e => e.TrimStart('.')).ToList();

        var storageProvider = mainWindow.StorageProvider;
        var result = await storageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Open Waveform File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Waveform Files")
                {
                    Patterns = extensions.Select(e => "*." + e).ToList()
                }
            }
        });

        if (result != null && result.Count > 0)
        {
            var file = result[0];
            var fileName = file.Path.LocalPath;
            var ext = Path.GetExtension(fileName).ToLower();
            _logger?.LogInformation("File selected: {FileName}", fileName);

            try
            {
                var factory = fileFactories.FirstOrDefault(f => f.SupportedExtensions.Contains(ext));
                if (factory == null) throw new NotSupportedException("Unsupported file format");

                // Використовуємо новий метод для безпечної заміни лоадера
                var newLoader = factory.CreateSource(fileName);
                SetCurrentLoader(newLoader);

                _logger?.LogDebug("Created waveform loader for file: {FileName}", fileName);

                await _currentLoader!.RunSetupAsync();
                _logger?.LogInformation("Loaded {ChannelCount} waveforms", _currentLoader.ChannelCount);

                Channels.Clear();
                for (int i = 0; i < _currentLoader.ChannelCount; i++)
                {
                    var meta = _currentLoader.GetMetadata(i);
                    var chVM = new ChannelViewModel(i, meta.ChannelName);
                    chVM.ChannelColor = GetChannelColor(i);
                    Channels.Add(chVM);
                }

                await RequestNewFrameAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading waveform file: {FileName}", fileName);
                throw;
            }
        }
        else
        {
            _logger?.LogDebug("File open dialog cancelled");
        }
    }

    private async void Capture()
    {
        _logger?.LogInformation("Capture initiated");

        var captureFactories = _sourceFactories.Where(f => f.IsCaptureSource).ToList();
        if (!captureFactories.Any())
        {
            _logger?.LogWarning("No capture sources available");
            return;
        }

        var factory = captureFactories.First();

        try
        {
            // Використовуємо новий метод для безпечної заміни лоадера
            var newLoader = factory.CreateSource("VISA_STUB_CONNECTION");
            SetCurrentLoader(newLoader);

            await _currentLoader!.RunSetupAsync();
            _logger?.LogInformation("Loaded {ChannelCount} waveforms from capture source", _currentLoader.ChannelCount);

            Channels.Clear();
            for (int i = 0; i < _currentLoader.ChannelCount; i++)
            {
                var meta = _currentLoader.GetMetadata(i);
                var chVM = new ChannelViewModel(i, meta.ChannelName);
                chVM.ChannelColor = GetChannelColor(i);
                Channels.Add(chVM);
            }

            await RequestNewFrameAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error starting capture");
        }
    }

    private async Task RequestNewFrameAsync()
    {
        if (_currentLoader == null) return;

        _renderCts?.Cancel();
        _renderCts = new CancellationTokenSource();
        var token = _renderCts.Token;

        IsBusy = true; // Показуємо загрузку

        var offset2 = TimeOffset + (TimePerDivision * -5);
        var timeEnd = offset2 + (TimePerDivision * 10);
        var viewport = new ViewportState(
            Time: new TimeRange(offset2, timeEnd),
            Voltage: new VoltageRange(-5f, 5f),
            Pan: new ViewportPan(0, 0),
            Zoom: new ViewportZoom(1.0, 1.0),
            ScreenWidthPx: ScreenWidthPx
        );

        try
        {
            var newFrames = await Task.Run(() =>
            {
                var results = new RenderFrame?[Channels.Count];
                for (int i = 0; i < Channels.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    if (!Channels[i].IsVisible) continue;

                    RenderFrame? result = null;
                    _currentLoader.ProcessChannelData(Channels[i].Index, viewport.Time, (span, in metadata, ct) =>
                    {
                        result = _pipeline.ProcessFrame(span, metadata, viewport, ct);
                    }, token);
                    results[i] = result;
                }
                return results;
            }, token);

            token.ThrowIfCancellationRequested();

            for (int i = 0; i < Channels.Count; i++)
            {
                Channels[i].CurrentFrame?.Dispose();
                Channels[i].CurrentFrame = newFrames[i];
            }
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("Frame rendering cancelled due to new viewport request.");
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsBusy = false; // Вимикаємо загрузку
            }
        }
    }
}
