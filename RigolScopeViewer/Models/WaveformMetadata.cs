// Метадані нашого сигналу (дуже легка структура)
public readonly struct WaveformMetadata
{
    public float StartTime { get; init; }
    public float SampleInterval { get; init; } // Це 1.0 / SampleRate
    public int TotalPoints { get; init; }
}