// 1. Сирі дані з VISA/CSV
using System;

public readonly struct SignalPoint
{
    public readonly double Time;
    public readonly float Voltage;
    
    public SignalPoint(double time, float voltage) => (Time, Voltage) = (time, voltage);
}

// 2. Результат біннінгу (підготовлено для рендеру)
public readonly struct ColumnStats
{
    public readonly Half Mean;
    public readonly Half StdDev;

    public ColumnStats(float mean, float stdDev)
    {
        (Mean, StdDev) = ((Half)mean, (Half)stdDev);
    }
}

// 3. Стан вікна перегляду (Viewport)
public record ViewportState(float TimeStart, float TimeEnd, float VoltageMin, float VoltageMax, int ScreenWidthPx);