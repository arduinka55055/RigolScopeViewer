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

namespace RigolScopeViewer;

public class ScopeGridControl : Control
{
    private const int HorizontalDivisions = 10;
    private const int VerticalDivisions = 8;
    private const int TicksPerDivision = 5;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var pen = new Pen(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), 1);
        var tickPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1);

        double colWidth = bounds.Width / HorizontalDivisions;
        double rowHeight = bounds.Height / VerticalDivisions;

        double centerRow = VerticalDivisions / 2.0;
        double centerCol = HorizontalDivisions / 2.0;

        // Draw vertical grid lines
        for (int i = 0; i <= HorizontalDivisions; i++)
        {
            double x = i * colWidth;
            context.DrawLine(pen, new Point(x, 0), new Point(x, bounds.Height));

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

        // Draw horizontal grid lines
        for (int i = 0; i <= VerticalDivisions; i++)
        {
            double y = i * rowHeight;
            context.DrawLine(pen, new Point(0, y), new Point(bounds.Width, y));

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
