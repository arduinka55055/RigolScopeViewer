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

    [ObservableProperty]
    private bool _isBusy; // Прив'яжи це до <ProgressBar IsIndeterminate="True" IsVisible="{Binding IsBusy}" />

    [ObservableProperty]
    private RenderFrame? _currentFrame; // Дані для нашого рендера

    public ObservableCollection<ChannelViewModel> Channels { get; } = [];
    public ICommand OpenCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }

    /// <summary>
    /// Constructor for dependency injection.
    /// Parameters can be null for backward compatibility/testing.
    /// </summary>
    public MainViewModel(
        ILogger<MainViewModel> logger,
        IConfigManager configManager,
        ILoggerFactory loggerFactory,
        IOscilloscopePipeline pipeline)
    {
        _logger = logger;
        _configManager = configManager;
        _pipeline = pipeline;
        _loggerFactory = loggerFactory;

        _logger.LogInformation("MainViewModel initialized");

        OpenCommand = new RelayCommand(OpenFile);
        ZoomInCommand = new RelayCommand(() => TimePerDivision *= 0.8);
        ZoomOutCommand = new RelayCommand(() => TimePerDivision *= 1.25);

    }

    // Команда, яку викликає GPUScopeControl при відпусканні миші або скролі
    [RelayCommand]
    private async Task ChangeViewport(ViewportChangeParams args)
    {
        // 1. Оновлюємо час на основі зуму (ZoomIn = менше секунд на екран)
        if (args.ZoomFactor != 1.0)
        {
            // Беремо твій поточний TimePerDivision і множимо
            TimePerDivision *= args.ZoomFactor;
        }

        // 2. Оновлюємо зміщення на основі панорамування (Pan)
        if (args.PanPercent != 0.0)
        {
            // Загальний час на екрані = TimePerDivision * 10 (якщо 10 клітинок)
            double totalScreenTime = TimePerDivision * 10.0;

            // Зміщуємо стартовий час на відповідну кількість секунд
            TimeOffset += totalScreenTime * args.PanPercent;
        }

        // 3. Оноалюємо ширину екрану в пікселях (це потрібно для правильного ресемплінгу)
        if (args.ScreenWidthPx > 0)
        {
            ScreenWidthPx = args.ScreenWidthPx;
        }

        // 4. Запитуємо новий кадр (твоя функція з Task.Run та IResampler)
        await RequestNewFrameAsync();
    }


    private async void OpenFile()
    {
        _logger?.LogInformation("OpenFile dialog initiated");

        var dlg = new OpenFileDialog();
        dlg.Filters.Add(new FileDialogFilter
        {
            Name = "Waveform Files",
            Extensions = { "bin", "csv" }
        });

        var result = await dlg.ShowAsync(new Window());
        if (result != null && result.Length > 0)
        {
            var fileName = result[0];
            _logger?.LogInformation("File selected: {FileName}", fileName);

            try
            {
                var binLogger = _loggerFactory?.CreateLogger<RigolBinSource>();
                var csvLogger = _loggerFactory?.CreateLogger<CsvWaveformSource>();

                _currentLoader = Path.GetExtension(fileName).ToLower() switch
                {
                    ".bin" => new RigolBinSource(fileName, binLogger),
                    ".csv" => new CsvWaveformSource(fileName, _configManager ?? throw new InvalidOperationException("ConfigManager not available"), csvLogger),
                    _ => throw new NotSupportedException("Unsupported file format")
                };

                _logger?.LogDebug("Created waveform loader for file: {FileName}", fileName);

                await _currentLoader.RunSetupAsync();
                _logger?.LogInformation("Loaded {ChannelCount} waveforms", _currentLoader.ChannelCount);

                Channels.Clear();
                for (int i = 0; i < _currentLoader.ChannelCount; i++)
                {
                    var meta = _currentLoader.GetMetadata(i);
                    Channels.Add(new ChannelViewModel(i, meta.ChannelName));
                }

                await RequestNewFrameAsync();
                //_waveforms = loader.Load(fileName);
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



    private async Task RequestNewFrameAsync()
    {
        if (_currentLoader == null) return;

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

        var newFrames = await Task.Run(() =>
        {
            var results = new RenderFrame?[Channels.Count];
            for (int i = 0; i < Channels.Count; i++)
            {
                if (!Channels[i].IsVisible) continue;

                RenderFrame? result = null;
                _currentLoader.ProcessChannelData(Channels[i].Index, viewport.Time, (span, in metadata) =>
                {
                    result = _pipeline.ProcessFrame(span, metadata, viewport);
                });
                results[i] = result;
            }
            return results;
        });

        for (int i = 0; i < Channels.Count; i++)
        {
            Channels[i].CurrentFrame?.Dispose();
            Channels[i].CurrentFrame = newFrames[i];
        }

        IsBusy = false; // Вимикаємо загрузку
    }
}

