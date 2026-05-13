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

namespace RigolScopeViewer.ViewModels;


public class MainViewModel : ViewModelBase
{
    private List<Waveform> _waveforms = new();
    private double _timePerDivision = 0.001; // 1ms/div
    private double _timeOffset;
    private double _triggerLevel;
    private bool _showTrigger = true;

    public ObservableCollection<ChannelViewModel> Channels { get; } = new();
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


    public MainViewModel()
    {
        OpenCommand = new RelayCommand(OpenFile);
        ZoomInCommand = new RelayCommand(() => TimePerDivision *= 0.8);
        ZoomOutCommand = new RelayCommand(() => TimePerDivision *= 1.25);

        // Demo data for testing
        _waveforms.Add(CreateSineWave("CH1", 1, 1000, 0, Colors.Yellow));
        _waveforms.Add(CreateSineWave("CH2", 0.5, 500, 0.5, Colors.Cyan));
        InitializeChannels();
    }

    private void InitializeChannels()
    {
        Channels.Clear();
        foreach (var waveform in _waveforms)
        {
            Channels.Add(new ChannelViewModel(waveform, UpdateWaveforms));
        }
        UpdateWaveforms();
    }

    private void UpdateWaveforms()
    {
        // Notify UI to redraw
        OnPropertyChanged(nameof(Waveforms));
    }

    public List<Waveform> Waveforms => _waveforms;

    private async void OpenFile()
    {
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

            if (Path.GetExtension(fileName).ToLower() == ".csv")
            {
                var debug = new CsvWaveformSource(fileName);
                debug.RunSetupAsync().Wait();
                Console.WriteLine($"Loaded {debug.ChannelCount} waveforms from CSV");
                debug.ProcessChannelData(0, 0, float.PositiveInfinity, (span, in metadata) =>
                {
                    Console.WriteLine($"Received {span.Length} points from CSV");
                    // Тут можна конвертувати span в double[] і створювати Waveform

                    // запихуємо спан у масив бо ми говнокодери
                    float[] floats = span.ToArray();
                    GodObject.ChannelDataReady = () => floats;
                    GodObject.WaveMetadata = metadata;
                });
                //InitializeChannels();
                return;
            }

            IWaveformLoader loader = Path.GetExtension(fileName).ToLower() switch
            {
                ".bin" => new RigolBinLoader(),
                ".csv" => new CsvLoader(),
                _ => throw new NotSupportedException("Unsupported file format")
            };

            _waveforms = loader.Load(fileName);
            InitializeChannels();
        }
    }

    private Waveform CreateSineWave(string name, double amplitude, double frequency,
                                   double phase, Color color)
    {
        var points = 1000;
        var timeData = new double[points];
        var analogData = new double[points];

        for (var i = 0; i < points; i++)
        {
            var t = i / (double)points * 0.01; // 10ms time window
            timeData[i] = t;
            analogData[i] = amplitude * Math.Sin(2 * Math.PI * frequency * t + phase);
        }

        return new Waveform
        {
            Name = name,
            Type = WaveformType.Analog,
            TimeData = timeData,
            AnalogData = analogData,
            Color = color
        };
    }
}
