// 1. Сирі дані з VISA/CSV
using System;
using System.Runtime.InteropServices;

public readonly struct SignalPoint
{
    public readonly double Time;
    public readonly float Voltage;

    public SignalPoint(double time, float voltage) => (Time, Voltage) = (time, voltage);
}

// 2. Результат біннінгу (підготовлено для рендеру)
[StructLayout(LayoutKind.Sequential)]
public readonly struct ColumnStats
{
    public readonly Half Mean;
    public readonly Half Min;
    public readonly Half Max;
    public readonly Half Alpha; // ПАДДІНГ: Робить структуру рівно 8 байт!

    public ColumnStats(float mean, float min, float max, float alpha = 1f)
    {
        Mean = (Half)mean;
        Min = (Half)min;
        Max = (Half)max;
        Alpha = (Half)alpha;
    }
}

// 3. Unified Viewport & Measurement Structures

/// <summary>
/// Represents panning (translation) in X and Y axes.
/// X: time axis pan (horizontal), Y: voltage axis pan (vertical).
/// </summary>
public readonly struct ViewportPan
{
    public readonly double X;
    public readonly double Y;

    public ViewportPan(double x = 0, double y = 0) => (X, Y) = (x, y);

    public override string ToString() => $"Pan(X={X}, Y={Y})";
}

/// <summary>
/// Represents scaling/zoom in X and Y axes.
/// X: time axis zoom (horizontal), Y: voltage axis zoom (vertical).
/// Values > 1.0 zoom in, < 1.0 zoom out.
/// </summary>
public readonly struct ViewportZoom
{
    public readonly double X;
    public readonly double Y;

    public ViewportZoom(double x = 1.0, double y = 1.0) => (X, Y) = (x, y);

    public override string ToString() => $"Zoom(X={X}, Y={Y})";
}

/// <summary>
/// Represents the voltage (vertical) measurement window.
/// </summary>
public readonly struct VoltageRange
{
    public readonly float Min;
    public readonly float Max;

    public VoltageRange(float min, float max) => (Min, Max) = (min, max);

    public float Span => Max - Min;

    public override string ToString() => $"Voltage({Min:F1}V - {Max:F1}V)";
}

/// <summary>
/// Represents the time (horizontal) measurement window.
/// </summary>
public readonly struct TimeRange
{
    public readonly double Start;
    public readonly double End;

    public TimeRange(double start, double end) => (Start, End) = (start, end);

    public double Duration => End - Start;

    public override string ToString() => $"Time({Start:F6}s - {End:F6}s)";
}

/// <summary>
/// Unified viewport state containing pan, zoom, time/voltage ranges, and screen dimensions.
/// Replaces the previous scattered parameters.
/// </summary>
public record ViewportState(
    TimeRange Time,
    VoltageRange Voltage,
    ViewportPan Pan,
    ViewportZoom Zoom,
    int ScreenWidthPx);
