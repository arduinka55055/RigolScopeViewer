using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using System;
using System.Runtime.InteropServices;



[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct ColumnStats
{
    public readonly Half Mean;    // Поле R в текстурі
    public readonly Half StdDev;  // Поле G в текстурі
    public readonly Half Pad1;    // Поле B
    public readonly Half Pad2;    // Поле A

    public ColumnStats(float mean, float stdDev)
    {
        Mean = (Half)mean;
        StdDev = (Half)stdDev;
        Pad1 = (Half)0f;
        Pad2 = (Half)1f; // Альфа-канал текстури краще тримати 1.0
    }
}


public class DpoDrawOperation : ICustomDrawOperation
{
    private static readonly SKRuntimeEffect DpoEffect = SKRuntimeEffect.CreateShader(@"
        uniform vec2 iResolution;
uniform vec2 iPan;        // Зміщення миші в пікселях під час drag
uniform float iZoomX;     // Зум по X під час drag (wheel)

uniform float iVoltsMin;  // Нижня межа екрану у вольтах
uniform float iVoltsMax;  // Верхня межа екрану у вольтах
uniform float iIntensity;

// Наша 1D текстура з даними бінів.
uniform shader iDataTexture; 

half4 main(vec2 fragCoord) {
    // 1. Fake Pan & Zoom (зміщуємо координати екрану)
    vec2 virtualCoord = fragCoord;
    virtualCoord.x = (virtualCoord.x - iPan.x) / iZoomX;
    
    // Отримуємо нормалізовані координати відносно оригінальної текстури
    vec2 uv = virtualCoord / iResolution;

    // Якщо ми витягнули графік за межі наявних даних - малюємо чорний фон
    if (uv.x < 0.0 || uv.x > 1.0) {
        return half4(0.0, 0.0, 0.0, 1.0);
    }

    // 2. Читаємо дані біна. 
    // eval() приймає координати в локальному просторі текстури (від 0 до Width)
    float sampleX = uv.x * iResolution.x;
    half4 binData = iDataTexture.eval(vec2(sampleX, 0.5));
    
    float mean = binData.r;
    float stdDev = binData.g;

    // Якщо даних немає (пустий бін), нічого не світиться
    if (stdDev <= 0.0) return half4(0.0, 0.0, 0.0, 1.0);

    // 3. Мапимо Y-піксель у Вольти
    // Skia має Y=0 зверху, тому інвертуємо UV по Y
    float vY = mix(iVoltsMin, iVoltsMax, 1.0 - uv.y);

    // 4. Математика DPO (Гауссіана)
    float diff = vY - mean;
    float exponent = -0.5 * (diff * diff) / (stdDev * stdDev);
    float alpha = exp(exponent) * iIntensity;
    
    alpha = clamp(alpha, 0.0, 1.0);

    // Колір фосфору (класичний осцилограф)
    vec3 color = vec3(1.0, 0.85, 0.1); 

    // Використовуємо alpha-premultiplied вивід для блендингу
    return half4(color * alpha, 1.0);
}
    ", out ShaderCreationErrors);
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

        if (DpoDrawOperation.ShaderCreationErrors != null){
            if(!ErrorShown)
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

        int width = (int)Bounds.Width;
        int height = (int)Bounds.Height;
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
            var info = new SKImageInfo(width, 1, SKColorType.RgbaF16, SKAlphaType.Unpremul, SKColorSpace.CreateSrgbLinear());
            _dataBitmap = new SKBitmap(info);
            _cachedWidth = width;
        }
    }

    // Це просто для тесту, у реальному житті сюди прилітатиме масив від IBinningEngine
    private unsafe void UpdateMockData(int width)
    {
        // Отримуємо прямий вказівник на пам'ять картинки
        ColumnStats* ptr = (ColumnStats*)_dataBitmap!.GetPixels();

        for (int i = 0; i < width; i++)
        {
            // Генеруємо синусоїду з "шумом"
            float x = i / (float)width * MathF.PI * 4f;
            float mean = MathF.Sin(x) * 1f + 0.5f; 
            
            // Робимо фронти розмитими (stdDev більше), а полиці чіткими
            float stdDev = 0.05f + MathF.Abs(MathF.Cos(x)) * 0.3f;

            // Записуємо напряму в пам'ять відео-текстури (0 allocations!)
            ptr[i] = new ColumnStats(mean, stdDev);
        }
    }

    public void Dispose() { }
    public bool Equals(ICustomDrawOperation? other) => false;
    public bool HitTest(Point p) => false;

}