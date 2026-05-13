using System;
using System.Numerics;
using RigolScopeViewer.Models;

namespace RigolScopeViewer.Interfaces;

public delegate void DataProcessor(ReadOnlySpan<float> data, in WaveformMetadata metadata);

public interface IWaveformSource : IDisposable
{
    // Скільки каналів знайдено у файлі / пам'яті / приладі
    int ChannelCount { get; }

    // Кожен канал може мати свій власний Sample Rate та кількість точок!
    WaveformMetadata GetMetadata(int channelIndex);

    event EventHandler? DataReady;

    // Запитуємо дані ДЛЯ КОНКРЕТНОГО КАНАЛУ
    void ProcessChannelData(int channelIndex, double startTime, double endTime, DataProcessor processor);
    void Start();
    void Stop();
}
