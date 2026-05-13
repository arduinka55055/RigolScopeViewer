using CommunityToolkit.Mvvm.ComponentModel;
using RigolScopeViewer.Models;
using System;
using System.ComponentModel;

namespace RigolScopeViewer.ViewModels;

public partial class ChannelViewModel(Waveform waveform, Action updateCallback) : ViewModelBase
{
    public string Name => waveform.Name;

    public bool IsVisible
    {
        get => waveform.IsVisible;
        set => SetProperty(waveform.IsVisible, value, waveform, (w, v) => w.IsVisible = v);
    }

    public double Scale
    {
        get => waveform.Scale;
        set => SetProperty(waveform.Scale, value, waveform, (w, v) => w.Scale = v);
    }

    public double VoltageOffset
    {
        get => waveform.VoltageOffset;
        set => SetProperty(waveform.VoltageOffset, value, waveform, (w, v) => w.VoltageOffset = v);
    }

    public double TimeOffset
    {
        get => waveform.TimeOffset;
        set => SetProperty(waveform.TimeOffset, value, waveform, (w, v) => w.TimeOffset = v);
    }

    // Override OnPropertyChanged ONLY to handle your global callback
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Ignore the "Name" property since it's read-only and doesn't update the model
        if (e.PropertyName != nameof(Name))
        {
            updateCallback?.Invoke();
        }
    }
}