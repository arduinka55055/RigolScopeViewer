using System;
using Microsoft.Extensions.Logging;
using RigolScopeViewer.Models;

namespace RigolScopeViewer.Services.Samplers;

public class DpoBinningEngine : IResampler<ColumnStats>
{
    private readonly ILogger<DpoBinningEngine>? _logger;

    public DpoBinningEngine(ILogger<DpoBinningEngine>? logger = null)
    {
        _logger = logger;
        _logger?.LogDebug("DpoBinningEngine initialized");
    }

    public void Resample(
        ReadOnlySpan<float> sourceVoltages,
        in WaveformMetadata metadata,
        TimeRange timeRange,
        Span<ColumnStats> destinationBins)
    {
        var binCount = destinationBins.Length;
        var timeSpan = timeRange.Duration;

        // Оскільки ми не можемо виділяти масиви через `new`, ми використовуємо stackalloc!
        // Це виділяє тимчасову пам'ять прямо в кеші процесора (на стеку), що працює миттєво.
        // 1080 * 8 байт = ~8 КБ, стек це легко проковтне.

        //TODO: check if replacing with ArrayPool is faster (for large screens)
        //Span<float> sums = stackalloc float[binCount];
        //Span<float> sumSqs = stackalloc float[binCount];
        //Span<int> counts = stackalloc int[binCount];

        //FIXME: use arrays so hot reload works
        float[] sums = new float[binCount];
        float[] sumSqs = new float[binCount];
        int[] counts = new int[binCount];

        // Оптимізація: Знаходимо стартовий та кінцевий індекси масиву, 
        // щоб не крутити цикл по мільйонах точок, які поза екраном.
        var startIndex = (int)((timeRange.Start - metadata.StartTime) / metadata.SampleInterval);
        var endIndex = (int)((timeRange.End - metadata.StartTime) / metadata.SampleInterval);

        // Захист від виходу за межі масиву
        startIndex = Math.Clamp(startIndex, 0, sourceVoltages.Length);
        endIndex = Math.Clamp(endIndex, 0, sourceVoltages.Length);

        // 1. Акумуляція даних
        for (var i = startIndex; i < endIndex; i++)
        {
            var voltage = sourceVoltages[i];
            var time = metadata.StartTime + (i * metadata.SampleInterval);

            var binIndex = (int)((time - timeRange.Start) / timeSpan * binCount);
            if (binIndex >= 0 && binIndex < binCount)
            {
                sums[binIndex] += voltage;
                sumSqs[binIndex] += voltage * voltage;
                counts[binIndex]++;
            }
        }

        // 2. Фінальний розрахунок та запис у destinationBins
        for (var i = 0; i < binCount; i++)
        {
            if (counts[i] == 0)
            {
                // Якщо в бін не потрапила жодна точка
                destinationBins[i] = new ColumnStats(float.NegativeInfinity, 0f);
                continue;
            }

            var mean = sums[i] / counts[i];
            var variance = (sumSqs[i] / counts[i]) - (mean * mean);
            var stdDev = (float)Math.Sqrt(Math.Max(0, variance));

            // ПАКУВАННЯ (як ми обговорювали раніше, для текстури відеокарти)
            // TODO: Якщо діапазон напруги відомий (наприклад, -100V до +100V), можна оптимізувати пакування, чекнути чи можна передати нормальний флоат а не [0,1] 
            const float PACK_RANGE = 200f;
            const float PACK_OFFSET = 100f;
            var packedMean = ((float)mean + PACK_OFFSET) / PACK_RANGE;
            var packedStdDev = stdDev / PACK_RANGE;

            // Записуємо напряму у вихідний Span
            destinationBins[i] = new ColumnStats(mean - 2, stdDev + 0.01f);
        }
    }
}
