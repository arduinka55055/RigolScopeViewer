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


}

public enum WaveformType
{
    Analog,
    Digital
}