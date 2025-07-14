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
        int w = (int)Math.Ceiling(width);
        int h = (int)Math.Ceiling(height);

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




    public void RenderWaveforms(List<Waveform> waveforms, double timePerDivision,
                               double timeOffset, double triggerLevel, bool showTrigger,
                               double cursorX1, double cursorX2, double cursorY1, double cursorY2)
    {
        if (_bitmap == null || waveforms.Count == 0) return;

        int width = _bitmap.PixelSize.Width;
        int height = _bitmap.PixelSize.Height;

        // Clear canvas
        _canvas.Clear(SKColors.Black);

        // Draw grid
        DrawGrid(width, height, timePerDivision, timeOffset);

        // Draw waveforms
        foreach (var waveform in waveforms.Where(w => w.IsVisible))
        {
            if (waveform.Type == WaveformType.Analog && waveform.AnalogData.Length > 0)
            {
                DrawAnalogWaveform(waveform, width, height, timePerDivision, timeOffset);
            }
            else if (waveform.Type == WaveformType.Digital && waveform.DigitalData.Length > 0)
            {
                DrawDigitalWaveform(waveform, width, height, timePerDivision, timeOffset);
            }
        }

        // Draw trigger line
        if (showTrigger)
        {
            DrawTriggerLine(height, triggerLevel);
        }

        // Draw cursors
        DrawCursors(width, height, cursorX1, cursorX2, cursorY1, cursorY2);

        // Update the image
        using (var lockedBitmap = _bitmap.Lock())
        {
            _surface.ReadPixels(new SKImageInfo(width, height), lockedBitmap.Address, lockedBitmap.RowBytes, 0, 0);
        }

        WaveformImage.Source = _bitmap;

        WaveformImage.InvalidateVisual();

    }

    private void DrawGrid(int width, int height, double timePerDivision, double timeOffset)
    {
        var paint = new SKPaint
        {
            Color = new SKColor(40, 40, 40),
            StrokeWidth = 1,
            IsAntialias = true
        };

        // Vertical lines (time divisions)
        double pixelsPerDivision = width / 10.0; // 10 divisions horizontally
        double startTime = timeOffset - timePerDivision * 5; // Center at offset

        for (int i = 0; i <= 10; i++)
        {
            float x = (float)(i * pixelsPerDivision);
            _canvas.DrawLine(x, 0, x, height, paint);
        }

        // Horizontal lines (voltage divisions)
        double voltsPerDivision = 1.0; // Default
        double pixelsPerVolt = height / 8.0; // 8 divisions vertically

        for (int i = 0; i <= 8; i++)
        {
            float y = (float)(i * pixelsPerVolt);
            _canvas.DrawLine(0, y, width, y, paint);
        }

        // Draw center lines thicker
        paint.Color = new SKColor(60, 60, 60);
        paint.StrokeWidth = 2;
        _canvas.DrawLine(width / 2, 0, width / 2, height, paint);
        _canvas.DrawLine(0, height / 2, width, height / 2, paint);
    }

    private void DrawAnalogWaveform(Waveform waveform, int width, int height,
                                   double timePerDivision, double timeOffset)
    {
        if (waveform.AnalogData.Length == 0) return;

        var paint = new SKPaint
        {
            Color = waveform.Color.ToSKColor(),
            StrokeWidth = 2,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        var path = new SKPath();
        double pixelsPerSecond = width / (timePerDivision * 10); // 10 divisions
        double voltsPerDivision = 1.0; // Default
        double pixelsPerVolt = height / 8.0; // 8 divisions

        double timeStart = timeOffset - timePerDivision * 5; // Center at offset
        double timeEnd = timeStart + timePerDivision * 10;

        bool firstPoint = true;

        for (int i = 0; i < waveform.TimeData.Length; i++)
        {
            double time = waveform.TimeData[i] + waveform.TimeOffset;
            if (time < timeStart || time > timeEnd) continue;

            double voltage = waveform.AnalogData[i] * waveform.Scale + waveform.VoltageOffset;
            float x = (float)((time - timeStart) * pixelsPerSecond);
            float y = (float)(height / 2 - voltage * pixelsPerVolt);

            if (firstPoint)
            {
                path.MoveTo(x, y);
                firstPoint = false;
            }
            else
            {
                path.LineTo(x, y);
            }
        }

        _canvas.DrawPath(path, paint);
    }

    private void DrawDigitalWaveform(Waveform waveform, int width, int height,
                                    double timePerDivision, double timeOffset)
    {
        if (waveform.DigitalData.Length == 0) return;

        var paint = new SKPaint
        {
            Color = waveform.Color.ToSKColor(),
            StrokeWidth = 2,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke
        };

        double pixelsPerSecond = width / (timePerDivision * 10); // 10 divisions
        double voltsPerDivision = 1.0; // Default
        double pixelsPerVolt = height / 8.0; // 8 divisions

        double timeStart = timeOffset - timePerDivision * 5; // Center at offset
        double timeEnd = timeStart + timePerDivision * 10;
        double timeIncrement = (waveform.TimeData[1] - waveform.TimeData[0]);

        // We'll draw each bit as a separate trace
        for (int bit = 0; bit < 8; bit++)
        {
            var path = new SKPath();
            double yPos = height - (bit + 1) * 20; // Position each digital trace

            bool firstPoint = true;
            bool lastState = false;

            for (int i = 0; i < waveform.TimeData.Length; i++)
            {
                double time = waveform.TimeData[i] + waveform.TimeOffset;
                if (time < timeStart || time > timeEnd) continue;

                byte sample = waveform.DigitalData[i];
                bool state = ((sample >> bit) & 1) == 1;
                float x = (float)((time - timeStart) * pixelsPerSecond);

                if (firstPoint)
                {
                    path.MoveTo(x, (float)(yPos - (state ? 10 : 0)));
                    firstPoint = false;
                }
                else
                {
                    // Transition line
                    if (state != lastState)
                    {
                        path.LineTo(x, (float)(yPos - (lastState ? 10 : 0)));
                        path.LineTo(x, (float)(yPos - (state ? 10 : 0)));
                    }
                }

                lastState = state;
            }

            // Draw the last segment
            if (!firstPoint)
            {
                path.LineTo(width, (float)(yPos - (lastState ? 10 : 0)));
            }

            _canvas.DrawPath(path, paint);
        }
    }

    private void DrawTriggerLine(int height, double triggerLevel)
    {
        var paint = new SKPaint
        {
            Color = new SKColor(255, 0, 0, 128),
            StrokeWidth = 1,
            IsAntialias = true
        };

        float y = (float)(height / 2 - triggerLevel * (height / 8.0));
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
                float x1 = (float)(cursorX1 * width);
                float x2 = (float)(cursorX2 * width);
                _canvas.DrawLine(x1, 0, x1, height, paint);
                _canvas.DrawLine(x2, 0, x2, height, paint);
            }

            // Draw horizontal cursors
            if (cursorY1 != cursorY2)
            {
                float y1 = (float)(cursorY1 * height);
                float y2 = (float)(cursorY2 * height);
                _canvas.DrawLine(0, y1, width, y1, paint);
                _canvas.DrawLine(0, y2, width, y2, paint);
            }
        }
    }
}