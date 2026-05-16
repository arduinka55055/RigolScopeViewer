using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RigolScopeViewer.Services;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using RigolScopeViewer.ViewModels;
using System.Globalization;

namespace RigolScopeViewer;

public class ScopeGridControl : Control
{
    private const int HorizontalDivisions = 10;
    private const int VerticalDivisions = 8;
    private const int TicksPerDivision = 5;

    public static readonly StyledProperty<double> TimePerDivisionProperty =
        AvaloniaProperty.Register<ScopeGridControl, double>(nameof(TimePerDivision), 0.001);

    public double TimePerDivision
    {
        get => GetValue(TimePerDivisionProperty);
        set => SetValue(TimePerDivisionProperty, value);
    }

    public static readonly StyledProperty<double> TimeOffsetProperty =
        AvaloniaProperty.Register<ScopeGridControl, double>(nameof(TimeOffset), 0.0);

    public double TimeOffset
    {
        get => GetValue(TimeOffsetProperty);
        set => SetValue(TimeOffsetProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<ChannelViewModel>?> ChannelsProperty =
        AvaloniaProperty.Register<ScopeGridControl, IEnumerable<ChannelViewModel>?>(nameof(Channels));

    public IEnumerable<ChannelViewModel>? Channels
    {
        get => GetValue(ChannelsProperty);
        set => SetValue(ChannelsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TimePerDivisionProperty || change.Property == TimeOffsetProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == ChannelsProperty)
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

    private void Channel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChannelViewModel.IsVisible) ||
            e.PropertyName == nameof(ChannelViewModel.Scale) ||
            e.PropertyName == nameof(ChannelViewModel.VoltageOffset) ||
            e.PropertyName == nameof(ChannelViewModel.IsActive) ||
            e.PropertyName == nameof(ChannelViewModel.ChannelColor))
        {
            InvalidateVisual();
        }
    }

    private string FormatSI(double value, string unit)
    {
        if (value == 0) return $"0.00 {unit}";
        double absValue = Math.Abs(value);
        string[] prefixes = { "f", "p", "n", "µ", "m", "", "k", "M", "G", "T" };
        int prefixIndex = 5;

        while (absValue >= 1000.0 && prefixIndex < prefixes.Length - 1)
        {
            absValue /= 1000.0;
            value /= 1000.0;
            prefixIndex++;
        }
        while (absValue < 1.0 && prefixIndex > 0)
        {
            absValue *= 1000.0;
            value *= 1000.0;
            prefixIndex--;
        }

        return $"{value:0.00}{prefixes[prefixIndex]}{unit}";
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var pen = new Pen(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), 1);
        var tickPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1);

        var typeface = new Typeface("Arial");

        double colWidth = bounds.Width / HorizontalDivisions;
        double rowHeight = bounds.Height / VerticalDivisions;

        double centerRow = VerticalDivisions / 2.0;
        double centerCol = HorizontalDivisions / 2.0;

        // Draw vertical grid lines (Time)
        for (int i = 0; i <= HorizontalDivisions; i++)
        {
            double x = i * colWidth;
            context.DrawLine(pen, new Point(x, 0), new Point(x, bounds.Height));

            // Draw text for time
            double timeValue = TimeOffset + (i * TimePerDivision) - TimePerDivision * 5;
            var textFormat = FormatSI(timeValue, "s");
            var ft = new FormattedText(
                textFormat,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                10,
                Brushes.LightGray
            );
            context.DrawText(ft, new Point(x + 2, bounds.Height - 15));

            // Draw ticks on the center horizontal line
            if (i < HorizontalDivisions)
            {
                double tickSpacing = colWidth / TicksPerDivision;
                for (int j = 1; j < TicksPerDivision; j++)
                {
                    double tickX = x + j * tickSpacing;
                    context.DrawLine(tickPen, new Point(tickX, centerRow * rowHeight - 3), new Point(tickX, centerRow * rowHeight + 3));
                }
            }
        }

        // Determine active channel for voltage ticks
        ChannelViewModel? activeChannel = Channels?.FirstOrDefault(c => c.IsActive && c.IsVisible);
        IBrush textBrush = Brushes.LightGray;
        if (activeChannel != null)
        {
            textBrush = new SolidColorBrush(activeChannel.ChannelColor);
        }

        // Draw horizontal grid lines (Voltage)
        for (int i = 0; i <= VerticalDivisions; i++)
        {
            double y = i * rowHeight;
            context.DrawLine(pen, new Point(0, y), new Point(bounds.Width, y));

            // Draw text for voltage
            if (activeChannel != null)
            {
                double voltageMax = (4.0 * activeChannel.Scale) + activeChannel.VoltageOffset;
                double voltageMin = -(4.0 * activeChannel.Scale) + activeChannel.VoltageOffset;

                // Value at this division (i = 0 is top, i = VerticalDivisions is bottom)
                double fraction = 1.0 - (double)i / VerticalDivisions; // 1.0 at top, 0.0 at bottom
                double voltageValue = voltageMin + fraction * (voltageMax - voltageMin);

                var textFormat = FormatSI(voltageValue, "V");
                var ft = new FormattedText(
                    textFormat,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    10,
                    textBrush
                );
                context.DrawText(ft, new Point(2, y + 2));
            }

            // Draw ticks on the center vertical line
            if (i < VerticalDivisions)
            {
                double tickSpacing = rowHeight / TicksPerDivision;
                for (int j = 1; j < TicksPerDivision; j++)
                {
                    double tickY = y + j * tickSpacing;
                    context.DrawLine(tickPen, new Point(centerCol * colWidth - 3, tickY), new Point(centerCol * colWidth + 3, tickY));
                }
            }
        }
    }
}

public partial class OscilloscopeControl : UserControl
{
    public OscilloscopeControl()
    {
        InitializeComponent();
    }
}
