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

public partial class OscilloscopeControlDebug : UserControl
{
    // Створюємо властивість, до якої прив'яжемо CurrentFrame з ViewModel
    public static readonly StyledProperty<RenderFrame?> FrameDataProperty =
        AvaloniaProperty.Register<OscilloscopeControlDebug, RenderFrame?>(nameof(FrameData));

    public RenderFrame? FrameData
    {
        get => GetValue(FrameDataProperty);
        set => SetValue(FrameDataProperty, value);
    }

    // Кешуємо логер як поле класу, щоб не діставати його з DI на кожному кадрі (це повільно)
    private readonly ILogger<DpoDrawOperation>? _drawLogger;

    public OscilloscopeControlDebug()
    {
        InitializeComponent();

        _drawLogger = App.Services?.GetService<ILogger<DpoDrawOperation>>();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Якщо прилетів новий кадр від ViewModel - кажемо UI перемалювати екран!
        if (change.Property == FrameDataProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (FrameData == null) return;
        // Request custom draw operation
        //context.Custom(new VoronoiDrawOperation(new Rect(0, 0, Bounds.Width, Bounds.Height), St.Elapsed.TotalSeconds));
        var operation = new DpoDrawOperation(
            _drawLogger,
            new Rect(Bounds.Size),
            FrameData,
            -5f, 5f, 2f, 2f, 2f // Твої uniforms
        );
        context.Custom(operation);

        // Schedule next frame for animation
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }
}
