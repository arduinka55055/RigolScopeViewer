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

namespace RigolScopeViewer.ViewModels;


public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private double _timePerDivision = 0.001; // 1ms/div

    [ObservableProperty]
    private double _timeOffset;

    [ObservableProperty]
    private double _triggerLevel;

    [ObservableProperty]
    private bool _showTrigger = true;

    [ObservableProperty]
    private int _screenWidthPx = 800; // Default fallback width

    private readonly ILogger<MainViewModel> _logger;
    private readonly IConfigManager _configManager;
    private readonly ILoggerFactory _loggerFactory;

    private readonly IOscilloscopePipeline _pipeline;
    private IWaveformSource? _currentLoader;
    private System.Threading.CancellationTokenSource? _renderCts;

    [ObservableProperty]
    private bool _isBusy; // Прив'яжи це до <ProgressBar IsIndeterminate="True" IsVisible="{Binding IsBusy}" />

    [ObservableProperty]
    private RenderFrame? _currentFrame; // Дані для нашого рендера

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
    /// Parameters can be null for backward compatibility/testing.
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

    // Команда, яку викликає GPUScopeControl при відпусканні миші або скролі
    [RelayCommand]
    private void ChangeViewport(ViewportChangeParams args)
    {
        _isUpdatingViewport = true;
        try
        {
            // 1. Оновлюємо час на основі зуму (ZoomIn = менше секунд на екран)
            if (args.ZoomFactor != 1.0)
            {
                // Беремо твій поточний TimePerDivision і множимо
                TimePerDivision *= args.ZoomFactor;
                
                // Constraints so we don't zoom out to infinity
                if (TimePerDivision > 10000.0) TimePerDivision = 10000.0;
                if (TimePerDivision < 1e-9) TimePerDivision = 1e-9;
            }

            // 2. Оновлюємо зміщення на основі панорамування (Pan)
            if (args.PanPercent != 0.0)
            {
                // Загальний час на екрані = TimePerDivision * 10 (якщо 10 клітинок)
                double totalScreenTime = TimePerDivision * 10.0;

                // Зміщуємо стартовий час на відповідну кількість секунд
                TimeOffset += totalScreenTime * args.PanPercent;
                
                // Constraint so we don't pan to infinity
                if (TimeOffset > 100000.0) TimeOffset = 100000.0;
                if (TimeOffset < -100000.0) TimeOffset = -100000.0;
            }

            // 3. Оноалюємо ширину екрану в пікселях (це потрібно для правильного ресемплінгу)
            if (args.ScreenWidthPx > 0)
            {
                ScreenWidthPx = args.ScreenWidthPx;
            }
        }
        finally
        {
            _isUpdatingViewport = false;
        }

        // 4. Запитуємо новий кадр (твоя функція з Task.Run та IResampler)
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

                _currentLoader?.Dispose();
                _currentLoader = factory.CreateSource(fileName);

                _logger?.LogDebug("Created waveform loader for file: {FileName}", fileName);

                await _currentLoader.RunSetupAsync();
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
                // We should probably show a message box here, but throw for now
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

        // For now just pick the first one, or we can prompt later.
        var factory = captureFactories.First();
        
        try
        {
            _currentLoader?.Dispose();
            _currentLoader = factory.CreateSource("VISA_STUB_CONNECTION");
            await _currentLoader.RunSetupAsync();
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
        _renderCts = new System.Threading.CancellationTokenSource();
        var token = _renderCts.Token;

        IsBusy = true; // Показуємо загрузку

        // Створюємо Viewport на основі твоїх налаштувань UI
        var timeEnd = TimeOffset + (TimePerDivision * 10); // Наприклад, 10 клітинок на екрані
        var viewport = new ViewportState(
            Time: new TimeRange(TimeOffset, timeEnd),
            Voltage: new VoltageRange(-5f, 5f), // Це можна брати з налаштувань каналу
            Pan: new ViewportPan(0, 0), // Pan is handled by GPU control now
            Zoom: new ViewportZoom(1.0, 1.0), // Zoom is handled by GPU control now
            ScreenWidthPx: ScreenWidthPx // Це можна брати з ActualWidth контрола
        );

        // ВАЖЛИВО: Span не може перетинати потоки. Тому ми запускаємо Task.Run,
        // всередині якого викликаємо лоадер. Лоадер дає Span, ми віддаємо його в пайплайн,
        // і результат (RenderFrame) повертаємо в UI потік. ZERO COPY!

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

            // Prevent race condition: If a new viewport change occurred while Task.Run was finishing,
            // we MUST discard this stale frame to avoid resetting the visual pan/zoom prematurely!
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
            // Only set IsBusy = false if this is still the active token (not overridden by a newer request)
            if (!token.IsCancellationRequested)
            {
                IsBusy = false; // Вимикаємо загрузку
            }
        }
    }
}