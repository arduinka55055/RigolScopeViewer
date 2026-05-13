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

namespace RigolScopeViewer.ViewModels;


public class MainViewModel : ViewModelBase
{
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
        InitializeChannels();
    }

    private void InitializeChannels()
    {
        Channels.Clear();

        UpdateWaveforms();
    }

    private void UpdateWaveforms()
    {
        // Notify UI to redraw
        //OnPropertyChanged(nameof(Waveforms));
    }


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

            IWaveformSource loader = Path.GetExtension(fileName).ToLower() switch
            {
                ".bin" => new RigolBinSource(fileName),
                ".csv" => new CsvWaveformSource(fileName),
                _ => throw new NotSupportedException("Unsupported file format")
            };
            await loader.RunSetupAsync();
            Console.WriteLine($"Loaded {loader.ChannelCount} waveforms");
            loader.ProcessChannelData(0, 0, float.PositiveInfinity, (span, in metadata) =>
            {
                Console.WriteLine($"Received {span.Length} points from loader");
                // Тут можна конвертувати span в double[] і створювати Waveform

                // запихуємо спан у масив бо ми говнокодери
                float[] floats = span.ToArray();
                GodObject.ChannelDataReady = () => floats;
                GodObject.WaveMetadata = metadata;
            });

            //_waveforms = loader.Load(fileName);
            InitializeChannels();
        }
    }

}
