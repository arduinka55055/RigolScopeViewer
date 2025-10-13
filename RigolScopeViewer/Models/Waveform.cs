using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RigolScopeViewer.Models;

public class Waveform
{
    public string Name { get; set; } = "Channel";
    public WaveformType Type { get; set; } = WaveformType.Analog;
    public double[] TimeData { get; set; } = Array.Empty<double>();
    public double[] AnalogData { get; set; } = Array.Empty<double>();
    public byte[] DigitalData { get; set; } = Array.Empty<byte>();
    public double TimeOffset { get; set; } = 0;
    public double VoltageOffset { get; set; } = 0;
    public double Scale { get; set; } = 1.0;
    public bool IsVisible { get; set; } = true;
    public Color Color { get; set; } = Colors.White;

    // Mipmaps: each level stores min/max pairs for downsampled segments
    public List<(double min, double max)[]> Mipmaps = new List<(double min, double max)[]>();

    public void BuildMipmaps(int levels = 20)
    {
        Mipmaps.Clear();
        var current = AnalogData.Select(v => v * Scale + VoltageOffset).ToArray();

        for (int level = 0; level < levels && current.Length > 1; level++)
        {
            int n = current.Length / 2;
            var mip = new (double min, double max)[n + current.Length % 2];

            for (int i = 0; i < n; i++)
            {
                double a = current[2 * i];
                double b = current[2 * i + 1];
                mip[i] = (Math.Min(a, b), Math.Max(a, b));
            }

            // Handle odd-length case
            if (current.Length % 2 != 0)
            {
                double last = current[^1];
                mip[^1] = (last, last);
            }

            Mipmaps.Add(mip);

            // Next level uses average of min/max to halve points
            current = mip.Select(p => (p.min + p.max) / 2).ToArray();
        }
    }


}

public enum WaveformType
{
    Analog,
    Digital
}