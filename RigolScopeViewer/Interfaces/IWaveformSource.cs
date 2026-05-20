using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using RigolScopeViewer.Models;

namespace RigolScopeViewer.Interfaces;

public delegate void DataProcessor(ReadOnlySpan<float> data, in WaveformMetadata metadata, CancellationToken cancellationToken);

public interface IWaveformSource : IDisposable
{
    // Скільки каналів знайдено у файлі / пам'яті / приладі
    int ChannelCount { get; }

    // Кожен канал може мати свій власний Sample Rate та кількість точок!
    WaveformMetadata GetMetadata(int channelIndex);

    event EventHandler? DataReady;

    // Якщо плагін має свій UI (кнопки), він повертає свій ViewModel. 
    // Якщо ні (як CSV) - повертає null.
    object? ControlPanelViewModel { get; }

    // Запитуємо дані ДЛЯ КОНКРЕТНОГО КАНАЛУ
    void ProcessChannelData(int channelIndex, TimeRange timeRange, DataProcessor processor, CancellationToken cancellationToken = default);
    void Start();
    void Stop();
    // Запускає налаштування джерела даних (наприклад, вибір файлу, підключення до приладу тощо)
    Task<bool> RunSetupAsync();
}
