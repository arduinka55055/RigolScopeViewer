using RigolScopeViewer.Models;
using System;

namespace RigolScopeViewer.ViewModels;

public class ChannelViewModel : ViewModelBase
{
    private readonly Waveform _waveform;
    private readonly Action _updateCallback;

    public string Name => _waveform.Name;
    public bool IsVisible
    {
        get => _waveform.IsVisible;
        set
        {
            _waveform.IsVisible = value;
            _updateCallback?.Invoke();
            OnPropertyChanged();
        }
    }

    public double Scale
    {
        get => _waveform.Scale;
        set
        {
            _waveform.Scale = value;
            _updateCallback?.Invoke();
            OnPropertyChanged();
        }
    }

    public double VoltageOffset
    {
        get => _waveform.VoltageOffset;
        set
        {
            _waveform.VoltageOffset = value;
            _updateCallback?.Invoke();
            OnPropertyChanged();
        }
    }

    public double TimeOffset
    {
        get => _waveform.TimeOffset;
        set
        {
            _waveform.TimeOffset = value;
            _updateCallback?.Invoke();
            OnPropertyChanged();
        }
    }

    public ChannelViewModel(Waveform waveform, Action updateCallback)
    {
        _waveform = waveform;
        _updateCallback = updateCallback;
    }
}