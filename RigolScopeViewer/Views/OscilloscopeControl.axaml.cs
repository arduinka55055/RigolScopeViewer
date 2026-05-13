using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using RigolScopeViewer.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;

namespace RigolScopeViewer;

public partial class OscilloscopeControl : UserControl
{
    private SKSurface _surface;
    private SKCanvas _canvas;
    private WriteableBitmap? _bitmap;

    private Rect _lastBounds;

    public OscilloscopeControl()
    {
        InitializeComponent();

        //ResizeCanvas(1000, 1000); // temp default size

        /*
        this.GetObservable(BoundsProperty)
        .Where(bounds => bounds.Width > 0 && bounds.Height > 0)
        .Subscribe(bounds =>
        {
            ResizeCanvas(bounds.Width, bounds.Height);
        });
        
        this.LayoutUpdated += (_, _) =>
        {
            var bounds = this.Bounds;
            if (bounds.Width > 0 && bounds.Height > 0 && bounds != _lastBounds)
            {
                _lastBounds = bounds;
                ResizeCanvas(bounds.Width, bounds.Height);
            }
        };*/
    }


    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        ResizeCanvas(size.Width, size.Height);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (finalSize.Width > 0 && finalSize.Height > 0)
        {
            ResizeCanvas(finalSize.Width, finalSize.Height);
        }
        return base.ArrangeOverride(finalSize);
    }



    private void ResizeCanvas(double width, double height)
    {
        var w = (int)Math.Ceiling(width);
        var h = (int)Math.Ceiling(height);

        if (w <= 0 || h <= 0)
        {
            _surface?.Dispose();
            _bitmap?.Dispose();
            _surface = null;
            _bitmap = null;
            _canvas = null;
            return;
        }

        _surface?.Dispose();
        //_bitmap?.Dispose();

        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);

        // Create the Skia surface with memory backing from the bitmap's buffer
        _bitmap = new WriteableBitmap(
            new PixelSize(w, h),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);

        using var fb = _bitmap.Lock();

        _surface = SKSurface.Create(info, fb.Address, fb.RowBytes);
        if (_surface == null)
        {
            _canvas = null;
            return;
        }

        _canvas = _surface.Canvas;
    }

    private void DrawTriggerLine(int height, double triggerLevel)
    {
        var paint = new SKPaint
        {
            Color = new SKColor(255, 0, 0, 128),
            StrokeWidth = 1,
            IsAntialias = true
        };

        var y = (float)(height / 2 - triggerLevel * (height / 8.0));
        _canvas.DrawLine(0, y, _bitmap!.PixelSize.Width, y, paint);
    }

    private void DrawCursors(int width, int height, double cursorX1, double cursorX2,
                            double cursorY1, double cursorY2)
    {
        if (cursorX1 != cursorX2 || cursorY1 != cursorY2)
        {
            var paint = new SKPaint
            {
                Color = new SKColor(0, 255, 255, 128),
                StrokeWidth = 1,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };

            // Draw vertical cursors
            if (cursorX1 != cursorX2)
            {
                var x1 = (float)(cursorX1 * width);
                var x2 = (float)(cursorX2 * width);
                _canvas.DrawLine(x1, 0, x1, height, paint);
                _canvas.DrawLine(x2, 0, x2, height, paint);
            }

            // Draw horizontal cursors
            if (cursorY1 != cursorY2)
            {
                var y1 = (float)(cursorY1 * height);
                var y2 = (float)(cursorY2 * height);
                _canvas.DrawLine(0, y1, width, y1, paint);
                _canvas.DrawLine(0, y2, width, y2, paint);
            }
        }
    }
}
