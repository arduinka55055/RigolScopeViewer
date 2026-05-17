using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RigolScopeViewer.Models;
using RigolScopeViewer.Services;

namespace RigolScopeViewer.ViewModels;

public partial class ChannelViewModel : ViewModelBase
{
    public int Index { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isActive = false;

    [ObservableProperty]
    private float _scale = 1.0f; // Volts per division

    [ObservableProperty]
    private float _voltageOffset = 0.0f;

    [ObservableProperty]
    private Color _channelColor = Colors.Yellow;

    [ObservableProperty]
    private RenderFrame? _currentFrame;

    public ChannelViewModel(int index, string name)
    {
        Index = index;
        _name = name;
    }
}
