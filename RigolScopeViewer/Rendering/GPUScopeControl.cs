using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RigolScopeViewer.Services;
using RigolScopeViewer.ViewModels;

namespace RigolScopeViewer;

public readonly struct ViewportChangeParams(double panPercent, double zoomFactor, int screenWidthPx)
{
    public double PanPercent { get; } = panPercent;
    public double ZoomFactor { get; } = zoomFactor;
    public int ScreenWidthPx { get; } = screenWidthPx;
}

public class GPUScopeControl : Control
{
    public static readonly StyledProperty<IEnumerable<ChannelViewModel>?> ChannelsProperty =
        AvaloniaProperty.Register<GPUScopeControl, IEnumerable<ChannelViewModel>?>(nameof(Channels));

    public IEnumerable<ChannelViewModel>? Channels
    {
        get => GetValue(ChannelsProperty);
        set => SetValue(ChannelsProperty, value);
    }

    public static readonly StyledProperty<ICommand?> UpdateViewportCommandProperty =
        AvaloniaProperty.Register<GPUScopeControl, ICommand?>(nameof(UpdateViewportCommand));

    public ICommand? UpdateViewportCommand
    {
        get => GetValue(UpdateViewportCommandProperty);
        set => SetValue(UpdateViewportCommandProperty, value);
    }

    private ViewportPan _pan = new(0.0, 0.0);
    private ViewportZoom _zoom = new(1.0, 1.0);
    private float _intensity = 2.0f;
    private bool _isDragging = false;
    private Point? _lastMousePosition;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ChannelsProperty)
        {
            if (change.OldValue is IEnumerable<ChannelViewModel> oldChannels)
            {
                if (oldChannels is INotifyCollectionChanged oldCollection)
                    oldCollection.CollectionChanged -= Channels_CollectionChanged;

                foreach (var ch in oldChannels)
                    ch.PropertyChanged -= Channel_PropertyChanged;
            }

            if (change.NewValue is IEnumerable<ChannelViewModel> newChannels)
            {
                if (newChannels is INotifyCollectionChanged newCollection)
                    newCollection.CollectionChanged += Channels_CollectionChanged;

                foreach (var ch in newChannels)
                    ch.PropertyChanged += Channel_PropertyChanged;
            }

            _pan = new(0.0, 0.0);
            _zoom = new(1.0, 1.0);
            InvalidateVisual();
        }
    }

    private void Channels_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (ChannelViewModel ch in e.OldItems)
                ch.PropertyChanged -= Channel_PropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (ChannelViewModel ch in e.NewItems)
                ch.PropertyChanged += Channel_PropertyChanged;
        }

        InvalidateVisual();
    }

    private double _uncommittedPanX = 0;

    private void Channel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChannelViewModel.IsVisible) || 
            e.PropertyName == nameof(ChannelViewModel.Scale) || 
            e.PropertyName == nameof(ChannelViewModel.VoltageOffset))
        {
            InvalidateVisual();
        }
        else if (e.PropertyName == nameof(ChannelViewModel.CurrentFrame))
        {
            _pan = new(0.0, _pan.Y);
            _zoom = new(1.0, _zoom.Y);
            _uncommittedPanX = 0;
            InvalidateVisual();
        }
    }

    private readonly ILogger<DpoDrawOperation> _drawLogger;

    public GPUScopeControl()
    {
        _drawLogger = App.Services!.GetRequiredService<ILogger<DpoDrawOperation>>();
    }

    public override void Render(DrawingContext context)
    {
        if (Channels == null) return;

        var bounds = new Rect(default, Bounds.Size);

        foreach (var channel in Channels)
        {
            if (channel == null || !channel.IsVisible || channel.CurrentFrame == null) continue;

            // Apply channel-specific voltage range/offset
            var voltageMin = -(4.0f * channel.Scale) + channel.VoltageOffset;
            var voltageMax = (4.0f * channel.Scale) + channel.VoltageOffset;
            var channelVoltage = new VoltageRange(voltageMin, voltageMax);

            context.Custom(new DpoDrawOperation(
                _drawLogger,
                bounds,
                channel.CurrentFrame,
                _pan,
                _zoom,
                channelVoltage,
                _intensity,
                channel.ChannelColor
            ));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);

        if (point.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _lastMousePosition = point.Position;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_isDragging && _lastMousePosition.HasValue)
        {
            var currentPosition = e.GetPosition(this);
            var deltaX = currentPosition.X - _lastMousePosition.Value.X;
            var deltaY = currentPosition.Y - _lastMousePosition.Value.Y;

            bool handledVertical = false;
            if (Math.Abs(deltaY) > 0 && Channels != null)
            {
                var activeChannel = Channels.FirstOrDefault(c => c.IsActive && c.IsVisible);
                if (activeChannel != null)
                {
                    // DeltaY > 0 means moving mouse down, which visually means the signal moves down
                    // Signal moves down -> VoltageOffset increases
                    double panPercentY = deltaY / Bounds.Height;
                    double totalVoltage = activeChannel.Scale * 8.0; // 8 vertical divisions
                    activeChannel.VoltageOffset += (float)(totalVoltage * panPercentY);
                    handledVertical = true;
                }
            }

            if (!handledVertical)
            {
                _pan = new(_pan.X + deltaX, _pan.Y + deltaY);
            }
            else
            {
                _pan = new(_pan.X + deltaX, _pan.Y);
            }

            _uncommittedPanX += deltaX;
            _lastMousePosition = currentPosition;

            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isDragging)
        {
            _isDragging = false;
            _lastMousePosition = null;
            e.Pointer.Capture(null);

            double panPercent = _uncommittedPanX / Bounds.Width;
            _uncommittedPanX = 0;

            if (Math.Abs(panPercent) > 0.0001)
            {
                var args = new ViewportChangeParams(-panPercent, 1.0, (int)Bounds.Width);

                if (UpdateViewportCommand?.CanExecute(args) == true)
                {
                    UpdateViewportCommand.Execute(args);
                }
            }
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var point = e.GetCurrentPoint(this).Position;

        // Scrolling up (Delta.Y > 0) means Zoom In.
        // Visually, the rendered waveform should stretch and become larger (renderZoom > 1.0).
        // Data-wise, the time window we load should shrink (dataZoom < 1.0).
        double renderZoom = Math.Pow(1.25, e.Delta.Y);
        double dataZoom = 1.0 / renderZoom;

        bool isVoltageZoom = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        if (isVoltageZoom)
        {
            // Update active channel's Scale and VoltageOffset instead of visual _zoom.Y
            if (Channels != null)
            {
                var activeChannel = Channels.FirstOrDefault(c => c.IsActive && c.IsVisible);
                if (activeChannel != null)
                {
                    double panPercentY = (point.Y / Bounds.Height) * (1.0 - renderZoom) / renderZoom;
                    double totalVoltage = activeChannel.Scale * 8.0; // 8 vertical divisions usually
                    
                    // Center around the mouse pointer
                    activeChannel.VoltageOffset += (float)(totalVoltage * panPercentY);
                    activeChannel.Scale *= (float)dataZoom;
                    
                    if (activeChannel.VoltageOffset > 10000f) activeChannel.VoltageOffset = 10000f;
                    if (activeChannel.VoltageOffset < -10000f) activeChannel.VoltageOffset = -10000f;
                    if (activeChannel.Scale > 10000f) activeChannel.Scale = 10000f;
                    if (activeChannel.Scale < 1e-6f) activeChannel.Scale = 1e-6f;
                }
            }
        }
        else
        {
            // Visual zoom for time (X axis) - keeping the mouse X position anchored
            double newPanX = point.X - (point.X - _pan.X) * renderZoom;
            _pan = new(newPanX, _pan.Y);
            _zoom = new(_zoom.X * renderZoom, _zoom.Y);

            // Calculate how much to shift the time offset so the data under the mouse pointer remains stable.
            // MainViewModel will calculate: TimeOffset_new = TimeOffset_old + TotalNewScreenTime * panPercent
            double panPercent = (point.X / Bounds.Width) * (1.0 - dataZoom) / dataZoom;
            var args = new ViewportChangeParams(panPercent, dataZoom, (int)Bounds.Width);

            if (UpdateViewportCommand?.CanExecute(args) == true)
            {
                UpdateViewportCommand.Execute(args);
            }
        }

        InvalidateVisual();
    }
}
