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

namespace RigolScopeViewer.ViewModels;


public class MainViewModel : ViewModelBase
{
    private double _timePerDivision = 0.001; // 1ms/div
    private double _timeOffset;
    private double _triggerLevel;
    private bool _showTrigger = true;

    private readonly ILogger<MainViewModel>? _logger;
    private readonly IConfigManager? _configManager;
    private readonly IResampler<ColumnStats>? _resampler;
    private readonly ILoggerFactory? _loggerFactory;

    public ObservableCollection<ChannelViewModel> Channels { get; } = [];
    public ICommand OpenCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }

    public double TimePerDivision
    {
        get => _timePerDivision;
        set
        {
            if (value > 0)
            {
                _timePerDivision = value;
                OnPropertyChanged();
                UpdateWaveforms();
            }
        }
    }

    public double TimeOffset
    {
        get => _timeOffset;
        set
        {
            _timeOffset = value;
            OnPropertyChanged();
            UpdateWaveforms();
        }
    }

    public double TriggerLevel
    {
        get => _triggerLevel;
        set
        {
            _triggerLevel = value;
            OnPropertyChanged();
            UpdateWaveforms();
        }
    }

    public bool ShowTrigger
    {
        get => _showTrigger;
        set
        {
            _showTrigger = value;
            OnPropertyChanged();
            UpdateWaveforms();
        }
    }

    /// <summary>
    /// Constructor for dependency injection.
    /// Parameters can be null for backward compatibility/testing.
    /// </summary>
    public MainViewModel(
        ILogger<MainViewModel>? logger = null,
        IConfigManager? configManager = null,
        IResampler<ColumnStats>? resampler = null,
        ILoggerFactory? loggerFactory = null)
    {
        _logger = logger;
        _configManager = configManager;
        _resampler = resampler;
        _loggerFactory = loggerFactory;

        _logger?.LogInformation("MainViewModel initialized");

        OpenCommand = new RelayCommand(OpenFile);
        ZoomInCommand = new RelayCommand(() => TimePerDivision *= 0.8);
        ZoomOutCommand = new RelayCommand(() => TimePerDivision *= 1.25);

        // Demo data for testing
        InitializeChannels();
    }

    private void InitializeChannels()
    {
        Channels.Clear();
        _logger?.LogDebug("Channels cleared and reinitialized");

        UpdateWaveforms();
    }

    private void UpdateWaveforms()
    {
        // Notify UI to redraw
        _logger?.LogDebug("UpdateWaveforms called with TimePerDivision={TimePerDivision}", _timePerDivision);
        //OnPropertyChanged(nameof(Waveforms));
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

                IWaveformSource loader = Path.GetExtension(fileName).ToLower() switch
                {
                    ".bin" => new RigolBinSource(fileName, binLogger),
                    ".csv" => new CsvWaveformSource(fileName, _configManager ?? throw new InvalidOperationException("ConfigManager not available"), csvLogger),
                    _ => throw new NotSupportedException("Unsupported file format")
                };

                _logger?.LogDebug("Created waveform loader for file: {FileName}", fileName);

                await loader.RunSetupAsync();
                _logger?.LogInformation("Loaded {ChannelCount} waveforms", loader.ChannelCount);

                loader.ProcessChannelData(0, 0, float.PositiveInfinity, (span, in metadata) =>
                {
                    _logger?.LogDebug("Received {PointCount} points from loader", span.Length);
                    // Тут можна конвертувати span в double[] і створювати Waveform

                    // запихуємо спан у масив бо ми говнокодери
                    float[] floats = span.ToArray();
                    GodObject.ChannelDataReady = () => floats;
                    GodObject.WaveMetadata = metadata;
                });

                //_waveforms = loader.Load(fileName);
                InitializeChannels();
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

}

