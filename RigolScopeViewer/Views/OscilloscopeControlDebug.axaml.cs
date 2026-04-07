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
        context.Custom(new VoronoiDrawOperation(new Rect(default, Bounds.Size), St.Elapsed.TotalSeconds));

        // Schedule next frame for animation
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }
}