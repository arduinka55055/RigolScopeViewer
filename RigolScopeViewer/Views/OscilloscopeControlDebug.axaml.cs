using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Reflection;

namespace RigolScopeViewer;

public partial class OscilloscopeControlDebug : UserControl
{


    public OscilloscopeControlDebug()
    {
        InitializeComponent();

    }
    private static readonly Stopwatch St = Stopwatch.StartNew();

    public override void Render(DrawingContext context)
    {
        // Request custom draw operation
        //context.Custom(new VoronoiDrawOperation(new Rect(0, 0, Bounds.Width, Bounds.Height), St.Elapsed.TotalSeconds));
        var operation = new DpoDrawOperation(new Rect(0, 0, Bounds.Width, Bounds.Height), 0, 0, -1, 1, 1);
        context.Custom(operation);

        // Schedule next frame for animation
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }
}
