using CommunityToolkit.Mvvm.ComponentModel;
using RigolScopeViewer.Models;
using System;
using System.ComponentModel;

namespace RigolScopeViewer.ViewModels;

public partial class ChannelViewModel(dynamic waveform, Action updateCallback) : ViewModelBase
{
    public string Name => waveform.Name;

    public bool IsVisible
    {
        get => waveform.IsVisible;
        //set => SetProperty(waveform.IsVisible, value, waveform, (w, v) => w.IsVisible = v);
        set { } //FIXME:
    }

    public double Scale
    {
        get => waveform.Scale;
        //set => SetProperty(waveform.Scale, value, waveform, (w, v) => w.Scale = v);
        set { }
    }

    public double VoltageOffset
    {
        get => waveform.VoltageOffset;
        //set => SetProperty(waveform.VoltageOffset, value, waveform, (w, v) => w.VoltageOffset = v);
        set { }
    }

    public double TimeOffset
    {
        get => waveform.TimeOffset;
        //set => SetProperty(waveform.TimeOffset, value, waveform, (w, v) => w.TimeOffset = v);
        set { }
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
