using System;
using RigolScopeViewer.Models;

/// <summary>
/// TOutput має бути unmanaged (blittable), щоб ідеально лягати в пам'ять відеокарти
/// </summary>
public interface IResampler<TOutput> where TOutput : unmanaged
{
    /// <summary>
    /// Виконує перетворення даних без аллокацій пам'яті.
    /// </summary>
    /// <param name="sourceVoltages">Сирі дані з осцилографа (вхід)</param>
    /// <param name="metadata">Часові характеристики сирих даних</param>
    /// <param name="viewportStartTime">Лівий край екрану (у секундах)</param>
    /// <param name="viewportEndTime">Правий край екрану (у секундах)</param>
    /// <param name="destinationBins">Буфер для запису результату (його довжина визначає кількість пікселів/бінів)</param>
    void Resample(
        ReadOnlySpan<float> sourceVoltages,
        in WaveformMetadata metadata,
        float viewportStartTime,
        float viewportEndTime,
        Span<TOutput> destinationBins);
}
