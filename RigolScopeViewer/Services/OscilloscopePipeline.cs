using System;
using System.Buffers;
using RigolScopeViewer.Models;

namespace RigolScopeViewer.Services;

public class OscilloscopePipeline(IResampler<ColumnStats> resampler)
{
    private readonly IResampler<ColumnStats> _resampler = resampler;

    // Викликається фоновим воркером, коли юзер зробив зум/пан, АБО прийшли нові дані
    public ColumnStats[] ProcessFrame(
        float[] rawOscilloscopeData,
        WaveformMetadata metadata,
        ViewportState viewport)
    {
        // 1. Беремо масив для результату з пулу (щоб не смітити в Garbage Collector)
        // ArrayPool може повернути масив БІЛЬШОГО розміру, ніж треба!
        var rentedArray = ArrayPool<ColumnStats>.Shared.Rent(viewport.ScreenWidthPx);

        try
        {
            // 2. Створюємо Span РІВНО того розміру, який потрібен (наприклад, 1080)
            var destinationSpan = rentedArray.AsSpan(0, viewport.ScreenWidthPx);

            // 3. Згодовуємо дані нашому швидкому Resampler-у
            _resampler.Resample(
                rawOscilloscopeData,
                metadata,
                viewport.TimeStart,
                viewport.TimeEnd,
                destinationSpan);

            // 4. Повертаємо масив (ВАЖЛИВО: Той, хто приймає цей масив, 
            // має потім повернути його назад у пул: ArrayPool.Shared.Return())
            return rentedArray;
        }
        catch
        {
            ArrayPool<ColumnStats>.Shared.Return(rentedArray);
            throw;
        }
    }
}
