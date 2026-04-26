using System;

public class DpoBinningEngine : IResampler<ColumnStats>
{
    public void Resample(
        ReadOnlySpan<float> sourceVoltages, 
        in WaveformMetadata metadata, 
        float viewportStartTime, 
        float viewportEndTime, 
        Span<ColumnStats> destinationBins)
    {
        int binCount = destinationBins.Length;
        float timeRange = viewportEndTime - viewportStartTime;

        // Оскільки ми не можемо виділяти масиви через `new`, ми використовуємо stackalloc!
        // Це виділяє тимчасову пам'ять прямо в кеші процесора (на стеку), що працює миттєво.
        // 1080 * 8 байт = ~8 КБ, стек це легко проковтне.

        //TODO: check if replacing with ArrayPool is faster (for large screens)
        Span<float> sums = stackalloc float[binCount];
        Span<float> sumSqs = stackalloc float[binCount];
        Span<int> counts = stackalloc int[binCount];

        // Оптимізація: Знаходимо стартовий та кінцевий індекси масиву, 
        // щоб не крутити цикл по мільйонах точок, які поза екраном.
        int startIndex = (int)((viewportStartTime - metadata.StartTime) / metadata.SampleInterval);
        int endIndex = (int)((viewportEndTime - metadata.StartTime) / metadata.SampleInterval);

        // Захист від виходу за межі масиву
        startIndex = Math.Clamp(startIndex, 0, sourceVoltages.Length);
        endIndex = Math.Clamp(endIndex, 0, sourceVoltages.Length);

        // 1. Акумуляція даних
        for (int i = startIndex; i < endIndex; i++)
        {
            float voltage = sourceVoltages[i];
            float time = metadata.StartTime + (i * metadata.SampleInterval);
            
            int binIndex = (int)((time - viewportStartTime) / timeRange * binCount);
            if (binIndex >= 0 && binIndex < binCount)
            {
                sums[binIndex] += voltage;
                sumSqs[binIndex] += voltage * voltage;
                counts[binIndex]++;
            }
        }

        // 2. Фінальний розрахунок та запис у destinationBins
        for (int i = 0; i < binCount; i++)
        {
            if (counts[i] == 0)
            {
                // Якщо в бін не потрапила жодна точка
                destinationBins[i] = new ColumnStats(float.NegativeInfinity, 0f);
                continue;
            }

            float mean = sums[i] / counts[i];
            float variance = (sumSqs[i] / counts[i]) - (mean * mean);
            float stdDev = (float)Math.Sqrt(Math.Max(0, variance));

            // ПАКУВАННЯ (як ми обговорювали раніше, для текстури відеокарти)
            const float PACK_RANGE = 200f;
            const float PACK_OFFSET = 100f;
            float packedMean = ((float)mean + PACK_OFFSET) / PACK_RANGE;
            float packedStdDev = stdDev / PACK_RANGE;

            // Записуємо напряму у вихідний Span
            destinationBins[i] = new ColumnStats(packedMean, packedStdDev);
        }
    }
}