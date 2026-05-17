using System.Numerics;

namespace RigolScopeViewer.Models;

public readonly struct WaveformMetadata
{
    public string ChannelName { get; init; }
    public float StartTime { get; init; }
    public float SampleInterval { get; init; } // Це 1.0 / SampleRate
    public int TotalPoints { get; init; }
    public Vector2 FitScreenTime => new(StartTime, StartTime + SampleInterval * TotalPoints);
}
