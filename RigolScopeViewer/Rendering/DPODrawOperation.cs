using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Microsoft.Extensions.Logging;
using RigolScopeViewer;
using RigolScopeViewer.Interfaces;
using RigolScopeViewer.Models;
using RigolScopeViewer.Services;
using RigolScopeViewer.Services.Samplers;
using SkiaSharp;
using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


public class DpoDrawOperation : ICustomDrawOperation
{
    private static readonly string shaderContents = Assembly.GetExecutingAssembly().GetManifestResourceStream("RigolScopeViewer.Shaders.DpoShader.glsl") switch
    {
        null => throw new Exception("Failed to load embedded shader resource."),
        var stream => new StreamReader(stream).ReadToEnd()
    };
    private static readonly SKRuntimeEffect DpoEffect = SKRuntimeEffect.CreateShader(shaderContents, out ShaderCreationErrors);
    private static string? ShaderCreationErrors;

    private static bool ErrorShown = false;

    public Rect Bounds { get; }

    // Uniforms
    public ViewportPan Pan { get; }
    public ViewportZoom Zoom { get; }
    public VoltageRange Voltage { get; }
    public float Intensity { get; }
    public Color ChannelColor { get; }

    // Глобальний кеш буфера (щоб не виділяти пам'ять на кожен кадр)
    // У реальному додатку це має бути в окремому сервісі, який керує ресурсами
    private static SKBitmap? _dataBitmap;
    private static int _cachedWidth;

    private readonly RenderFrame _frame;

    private readonly ILogger<DpoDrawOperation> _logger;
    private readonly IAlertModal _alertModal;

    public DpoDrawOperation(ILogger<DpoDrawOperation> logger, IAlertModal alertModal, Rect bounds, RenderFrame frame, ViewportPan pan, ViewportZoom zoom, VoltageRange voltage, float intensity, Color color)
    {
        Bounds = bounds;
        _frame = frame;
        _logger = logger;
        _alertModal = alertModal;
        Pan = pan;
        Zoom = zoom;
        Voltage = voltage;
        Intensity = intensity;
        ChannelColor = color;


        if (ShaderCreationErrors != null)
        {
            if (!ErrorShown)
            {
                ErrorShown = true;
                _alertModal.Show("Shader Compilation Error", $"Failed to create shader:\n{DpoDrawOperation.ShaderCreationErrors}");
                Console.WriteLine($"Shader errors: {DpoDrawOperation.ShaderCreationErrors}");
            }
        }
    }

    public void Render(ImmediateDrawingContext context)
    {
        var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (lease == null) return;

        using var skia = lease.Lease();
        var canvas = skia.SkCanvas;

        var width = (int)Bounds.Width;
        var height = (int)Bounds.Height;
        if (width <= 0 || height <= 0) return;

        // 1. Керування пам'яттю текстури
        EnsureBitmapSize(width);

        // 2. Симуляція заповнення даних (У тебе це робитиме Background Worker)
        UpdateBufferData();

        canvas.Save();
        canvas.Translate((float)Bounds.Left, (float)Bounds.Top);

        try
        {
            if (DpoEffect == null || _dataBitmap == null) return;

            // Створюємо шейдер з нашої картинки (Clamp запобігає перетіканню текстури по краях)
            using var dataImage = SKImage.FromPixels(_dataBitmap.Info, _dataBitmap.GetPixels(), _dataBitmap.RowBytes);
            using var dataShader = dataImage.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));

            var children = new SKRuntimeEffectChildren(DpoEffect)
            {
                ["iDataTexture"] = dataShader
            };

            var inputs = new SKRuntimeEffectUniforms(DpoEffect)
            {
                ["iResolution"] = new[] { (float)width, (float)height },
                ["iPan"] = new[] { (float)Pan.X, (float)Pan.Y },
                ["iZoom"] = new[] { (float)Zoom.X, (float)Zoom.Y },
                ["iVoltsMin"] = Voltage.Min,
                ["iVoltsMax"] = Voltage.Max,
                ["iIntensity"] = Intensity,
                ["iColor"] = new[] { ChannelColor.R / 255f, ChannelColor.G / 255f, ChannelColor.B / 255f }
            };

            using var shader = DpoEffect.ToShader(uniforms: inputs, children: children);
            using var paint = new SKPaint
            {
                Shader = shader,
                Style = SKPaintStyle.Fill
            };

            // 3. Малюємо екран
            canvas.DrawRect(0, 0, width, height, paint);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void EnsureBitmapSize(int width)
    {
        if (_dataBitmap == null || _cachedWidth != width)
        {
            _dataBitmap?.Dispose();
            // RgbaF32: 4 float-канали на піксель = ідеально для наших даних
            var info = new SKImageInfo(width, 1, SKColorType.RgbaF16, SKAlphaType.Unpremul, SKColorSpace.CreateSrgbLinear());
            _dataBitmap = new SKBitmap(info);
            _cachedWidth = width;
        }
    }

    // Це просто для тесту, у реальному житті сюди прилітатиме масив від IBinningEngine
    private void UpdateBufferData()
    {
        // Отримуємо прямий (безпечний) вказівник на пам'ять картинки
        var sourceSpan = _frame.GetValidSpan();

        // Отримуємо пам'ять текстури Skia
        var bytePtr = _dataBitmap!.GetPixelSpan();
        var destSpan = MemoryMarshal.Cast<byte, ColumnStats>(bytePtr);

        // БЛИСКАВИЧНЕ КОПІЮВАННЯ (На рівні C++, без циклів for!)
        if (sourceSpan.Length > destSpan.Length)
        {
            _logger.LogWarning("Warning: Source data is larger than texture capacity. Data will be truncated.");
            sourceSpan = sourceSpan[..destSpan.Length];
        }
        sourceSpan.CopyTo(destSpan);

    }

    public void Dispose() => GC.SuppressFinalize(this);
    public bool Equals(ICustomDrawOperation? other) => false;
    public bool HitTest(Point p) => Bounds.Contains(p);

}
