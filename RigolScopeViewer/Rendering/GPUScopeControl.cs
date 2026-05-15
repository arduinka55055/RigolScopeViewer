using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
                _intensity
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

            _pan = new(_pan.X + deltaX, _pan.Y + deltaY);
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
        double zoomFactor = e.Delta.Y > 0 ? 0.8 : 1.25;

        bool isVoltageZoom = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        if (isVoltageZoom)
        {
            double newPanY = point.Y - (point.Y - _pan.Y) * zoomFactor;
            _pan = new(_pan.X, newPanY);
            _zoom = new(_zoom.X, _zoom.Y * zoomFactor);
        }
        else
        {
            double newPanX = point.X - (point.X - _pan.X) * zoomFactor;
            _pan = new(newPanX, _pan.Y);
            _zoom = new(_zoom.X * zoomFactor, _zoom.Y);

            double panPercent = (point.X / Bounds.Width) * (1.0 - zoomFactor) / zoomFactor;
            var args = new ViewportChangeParams(panPercent, zoomFactor, (int)Bounds.Width);

            if (UpdateViewportCommand?.CanExecute(args) == true)
            {
                UpdateViewportCommand.Execute(args);
            }
        }

        InvalidateVisual();
    }
}
