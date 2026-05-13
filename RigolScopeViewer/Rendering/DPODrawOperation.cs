using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using RigolScopeViewer;
using RigolScopeViewer.Models;
using SkiaSharp;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;



[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct ColumnStatsOLd(float mean, float stdDev)
{
    public readonly Half Mean = (Half)mean;    // Поле R в текстурі
    public readonly Half StdDev = (Half)stdDev;  // Поле G в текстурі
    public readonly Half Pad1 = (Half)0f;    // Поле B
    public readonly Half Pad2 = (Half)1f;    // Поле A
}


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
    public double PanX { get; }
    public double ZoomX { get; }
    public float VoltsMin { get; }
    public float VoltsMax { get; }
    public float Intensity { get; }

    // Глобальний кеш буфера (щоб не виділяти пам'ять на кожен кадр)
    // У реальному додатку це має бути в окремому сервісі, який керує ресурсами
    private static SKBitmap? _dataBitmap;
    private static int _cachedWidth;

    public DpoDrawOperation(Rect bounds, double panX, double zoomX, float vMin, float vMax, float intensity)
    {
        Bounds = bounds;
        PanX = panX;
        ZoomX = zoomX;
        VoltsMin = vMin;
        VoltsMax = vMax;
        Intensity = intensity;

        if (ShaderCreationErrors != null)
        {
            if (!ErrorShown)
            {
                ErrorShown = true;
                AvaloniaMessageBox.ShowCustomMessageBox("Shader Compilation Error", $"Failed to create shader:\n{DpoDrawOperation.ShaderCreationErrors}");
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
        UpdateMockData(width);

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
                ["iPan"] = new[] { (float)PanX, 0f },
                ["iZoomX"] = (float)ZoomX,
                ["iVoltsMin"] = VoltsMin,
                ["iVoltsMax"] = VoltsMax,
                ["iIntensity"] = Intensity
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
            var info = new SKImageInfo(width, 1, SKColorType.RgF16, SKAlphaType.Unpremul, SKColorSpace.CreateSrgbLinear());
            _dataBitmap = new SKBitmap(info);
            _cachedWidth = width;
        }
    }

    // Це просто для тесту, у реальному житті сюди прилітатиме масив від IBinningEngine
    private void UpdateMockData(int width)
    {
        // Отримуємо прямий (безпечний) вказівник на пам'ять картинки
        var bytePtr = _dataBitmap!.GetPixelSpan();
        var ptr = MemoryMarshal.Cast<byte, ColumnStats>(bytePtr);

        for (var i = 0; i < width; i++)
        {
            // Генеруємо синусоїду з "шумом"
            var x = i / (float)width * MathF.PI * 4f;
            var mean = MathF.Sin(x) * 1f + 0.5f;

            // Робимо фронти розмитими (stdDev більше), а полиці чіткими
            var stdDev = 0.05f + MathF.Abs(MathF.Cos(x)) * 0.3f;

            // Записуємо напряму в пам'ять відео-текстури (0 allocations!)
            ptr[i] = new ColumnStats(mean, stdDev);
        }

        // use dpobinningengine (debug)
        if (GodObject.ChannelDataReady == null) return;
        float[] rawData = GodObject.ChannelDataReady();
        var engine = new DpoBinningEngine();
        engine.Resample(rawData, GodObject.WaveMetadata, 0, 100000, ptr);

    }

    public void Dispose() => GC.SuppressFinalize(this);
    public bool Equals(ICustomDrawOperation? other) => false;
    public bool HitTest(Point p) => Bounds.Contains(p);

}
