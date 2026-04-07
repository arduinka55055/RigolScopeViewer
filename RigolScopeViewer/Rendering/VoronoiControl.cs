using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RigolScopeViewer;

public class VoronoiControl : Control
{
    private static readonly Stopwatch St = Stopwatch.StartNew();
    private bool _isRunning;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
        // Start the animation loop when the control appears
        _isRunning = true;
        QueueNextFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        
        // STOP the loop when the control is removed (prevents memory leaks/ghost CPU usage)
        _isRunning = false;
    }

    private void QueueNextFrame()
    {
        if (!_isRunning) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
        {
            // Syncs the redraw exactly to the monitor's refresh rate
            topLevel.RequestAnimationFrame(_ =>
            {
                if (!_isRunning) return;
                InvalidateVisual(); // Trigger Render()
                QueueNextFrame();   // Loop
            });
        }
    }

    public override void Render(DrawingContext context)
    {
        // Draw the current frame
        var rect = new Rect(default, Bounds.Size);
        context.Custom(new VoronoiDrawOperation(rect, St.Elapsed.TotalSeconds));
    }
}