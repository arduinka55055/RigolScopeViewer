using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

public class VoronoiDrawOperation(Rect bounds, double time) : ICustomDrawOperation
{
    private static readonly SKRuntimeEffect VoronoiEffect = SKRuntimeEffect.CreateShader(
        @"// Voronoi + Triangle Mask Shader
uniform float iTime;
uniform vec2 iResolution;

// Simple hash for Voronoi
vec2 hash(vec2 p) {
    p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
    return fract(sin(p) * 43758.5453);
}

half4 main(vec2 fragCoord) {
    vec2 uv = fragCoord / iResolution.xy;
    
    // Voronoi Logic
    vec2 p = uv * 8.0;
    vec2 i_p = floor(p);
    vec2 f_p = fract(p);
    float minDist = 1.0;

    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            vec2 neighbor = vec2(float(x), float(y));
            vec2 point = hash(i_p + neighbor);
            point = 0.5 + 0.5 * sin(iTime + 6.2831 * point);
            float dist = length(neighbor + point - f_p);
            minDist = min(minDist, dist);
        }
    }

    // Output color based on Voronoi distance
    return half4(vec3(1.0 - minDist) * vec3(0.2, 0.5, 0.9), 1.0);
}", out var errors);

    public Rect Bounds { get; } = bounds;
    public double Time { get; } = time;

    Rect ICustomDrawOperation.Bounds => Bounds;

    public void Render(ImmediateDrawingContext context)
    {
        var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (lease == null) return;

        using var skia = lease.Lease();
        var canvas = skia.SkCanvas;

        // 1. SAVE THE STATE
        canvas.Save();

        // 2. MOVE TO THE CONTROL'S STARTING POINT
        // This ensures (0,0) in your SKPath is actually the Top-Left of your control
        canvas.Translate((float)Bounds.Left, (float)Bounds.Top);

        try
        {
            // Double-check the effect compiled
            if (VoronoiEffect == null) return;

            var inputs = new SKRuntimeEffectUniforms(VoronoiEffect)
            {
                ["iTime"] = (float)Time,
                ["iResolution"] = new[] { (float)Bounds.Width, (float)Bounds.Height }
            };

            // Note: 'false' for isOpaque is safer for testing
            using var shader = VoronoiEffect.ToShader(inputs);
            using var paint = new SKPaint
            {
                Shader = shader,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            // 3. DEFINE THE TRIANGLE (Local to the control)
            using var path = new SKPath();
            var w = (float)Bounds.Width;
            var h = (float)Bounds.Height;

            path.MoveTo(w / 2, 0);       // Top Middle
            path.LineTo(0, h);           // Bottom Left
            path.LineTo(w, h);           // Bottom Right
            path.Close();

            canvas.DrawPath(path, paint);
        }
        finally
        {
            // 4. RESTORE STATE
            canvas.Restore();
        }
    }

    public void Dispose() { }
    public bool Equals(ICustomDrawOperation? other) => false;
    public bool HitTest(Point p) => Bounds.Contains(p);
}
