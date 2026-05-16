using System;
using System.Runtime.CompilerServices;
using System.Threading;
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
        Span<ColumnStats> destinationBins,
        CancellationToken cancellationToken = default)
    {
        var numBins = destinationBins.Length; // Кількість колонок (пікселів по ширині) на екрані
        if (numBins == 0 || sourceVoltages.Length < 2)
        {
            // Повертаємо порожні колонки (min > max сигналізує шейдеру про відсутність даних)
            destinationBins.Fill(new ColumnStats(0, 1, 0, 0));
            return;
        }
        var timeDiv = timeRange.Duration / 10; //10 клітинок на весь екран

        double startTime = metadata.StartTime;
        double sampleInterval = metadata.SampleInterval;
        double relativeStartTime = metadata.StartTime - timeRange.Start;
        var visibleStartTime = timeRange.Start;
        var visibleEndTime = timeRange.End;

        // Загальний час, який зараз видно на екрані
        var visibleDuration = visibleEndTime - visibleStartTime;

        if (visibleDuration <= 0.0)
        {
            destinationBins.Fill(new ColumnStats(0, 1, 0, 0));
            return;
        }

        //debug log everything
        _logger?.LogDebug("Resampling with DpoBinningEngine:");
        _logger?.LogDebug("Source voltages length: {Length}", sourceVoltages.Length);
        _logger?.LogDebug("Metadata: StartTime={StartTime}, SampleInterval={SampleInterval}, TotalPoints={TotalPoints}", metadata.StartTime, metadata.SampleInterval, metadata.TotalPoints);
        _logger?.LogDebug("TimeRange: Start={Start}, End={End}", timeRange.Start, timeRange.End);
        _logger?.LogDebug("Calculated visible duration: {VisibleDuration}", visibleDuration);


        // Скільки секунд реального часу "вміщується" в одну колонку (один піксель)
        var binWidthTime = visibleDuration / numBins;

        // Масиви-акумулятори для кожної екранної колонки
        var binMinVoltages = new float[numBins];
        var binMaxVoltages = new float[numBins];

        // Інтеграл напруги за часом (Площа під графіком = Вольти * Секунди)
        var voltageTimeIntegral = new double[numBins];
        // Загальний час знаходження сигналу всередині конкретної колонки
        var accumulatedTime = new double[numBins];

        // Ініціалізуємо екстремуми екстремальними значеннями для подальшого пошуку Min/Max
        Array.Fill(binMinVoltages, float.MaxValue);
        Array.Fill(binMaxVoltages, float.MinValue);

        // Проходимо по кожному відрізку (парі сусідніх точок у сирому сигналі)
        for (var i = 0; i < sourceVoltages.Length - 1; i++)
        {
            // Перевірка скасування кожні 4096 ітерацій (для оптимізації продуктивності)
            if ((i & 0xFFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            // Обчислюємо абсолютний час для двох сусідніх точок (A та B)
            var sampleTimeA = startTime + i * sampleInterval;
            var sampleTimeB = startTime + (i + 1) * sampleInterval;
            var voltageA = sourceVoltages[i];
            var voltageB = sourceVoltages[i + 1];

            // Впорядковуємо точки за часом (на випадок, якщо sampleInterval від'ємний)
            var segmentStartTime = Math.Min(sampleTimeA, sampleTimeB);
            var segmentEndTime = Math.Max(sampleTimeA, sampleTimeB);
            var segmentStartVoltage = sampleTimeA <= sampleTimeB ? voltageA : voltageB;
            var segmentEndVoltage = sampleTimeA <= sampleTimeB ? voltageB : voltageA;

            // 1. ВІДСІКАННЯ (Culling): 
            // Якщо весь відрізок лежить лівіше або правіше видимої зони екрану - ігноруємо його
            if (segmentEndTime < visibleStartTime || segmentStartTime > visibleEndTime)
                continue;

            // 2. ОБРІЗАННЯ (Clipping): 
            // Якщо відрізок частково виходить за межі екрану, обрізаємо його по краях екрану
            var clippedStartTime = Math.Max(segmentStartTime, visibleStartTime);
            var clippedEndTime = Math.Min(segmentEndTime, visibleEndTime);

            if (clippedEndTime == visibleEndTime)
                clippedEndTime = visibleEndTime - 1e-12 * binWidthTime;
            // -------------------------------------

            if (clippedEndTime <= clippedStartTime)
                continue;

            // 3. ЛІНІЙНА ІНТЕРПОЛЯЦІЯ (Lerp) для обрізаних країв:
            // Шукаємо, яку частку (від 0.0 до 1.0) становить обрізаний час відносно оригінального відрізка.
            // Додаємо 1e-300 у знаменник, щоб уникнути помилки ділення на нуль, якщо точки мають однаковий час.
            var clipRatioStart = (clippedStartTime - segmentStartTime) / (segmentEndTime - segmentStartTime + 1e-300);
            var clipRatioEnd = (clippedEndTime - segmentStartTime) / (segmentEndTime - segmentStartTime + 1e-300);

            // Розраховуємо напругу в точках обрізки за формулою: V = V_start + (V_end - V_start) * Ratio
            var clippedStartVoltage = (float)(segmentStartVoltage + (segmentEndVoltage - segmentStartVoltage) * clipRatioStart);
            var clippedEndVoltage = (float)(segmentStartVoltage + (segmentEndVoltage - segmentStartVoltage) * clipRatioEnd);

            // 4. ВИЗНАЧЕННЯ КОЛОНОК (Bins):
            // Переводимо час у "координати" колонок. Ділимо час на ширину колонки.
            var floatBinIndexStart = (clippedStartTime - visibleStartTime) / binWidthTime;
            var floatBinIndexEnd = (clippedEndTime - visibleStartTime) / binWidthTime;

            // Округлюємо до цілих індексів масиву та обмежуємо (Clamp), щоб не вийти за межі
            var binStartIndex = Math.Clamp((int)Math.Floor(floatBinIndexStart), 0, numBins - 1);
            var binEndIndex = Math.Clamp((int)Math.Floor(floatBinIndexEnd), 0, numBins - 1);

            // 5. РАСТЕРИЗАЦІЯ ВІДРІЗКА (розподіл сигналу по пікселях):
            // Відрізок може перетинати кілька колонок екрану. Проходимо по кожній з них.
            for (var b = binStartIndex; b <= binEndIndex; b++)
            {
                // Часові межі поточної колонки (пікселя)
                var currentBinStartTime = visibleStartTime + b * binWidthTime;
                var currentBinEndTime = currentBinStartTime + binWidthTime;

                // Знаходимо, яка саме частина відрізка потрапляє В СЕРЕДИНУ цієї конкретної колонки
                var intersectStartTime = Math.Max(clippedStartTime, currentBinStartTime);
                var intersectEndTime = Math.Min(clippedEndTime, currentBinEndTime);

                if (intersectStartTime >= intersectEndTime) continue;

                // Знову лінійна інтерполяція, щоб знайти напругу на вході в колонку і на виході з неї
                var intersectRatioStart = (intersectStartTime - clippedStartTime) / (clippedEndTime - clippedStartTime + 1e-300);
                var intersectRatioEnd = (intersectEndTime - clippedStartTime) / (clippedEndTime - clippedStartTime + 1e-300);

                var intersectStartVoltage = (float)(clippedStartVoltage + (clippedEndVoltage - clippedStartVoltage) * intersectRatioStart);
                var intersectEndVoltage = (float)(clippedStartVoltage + (clippedEndVoltage - clippedStartVoltage) * intersectRatioEnd);

                // 6. ОНОВЛЕННЯ СТАТИСТИКИ КОЛОНКИ:
                var segmentMinInBin = Math.Min(intersectStartVoltage, intersectEndVoltage);
                var segmentMaxInBin = Math.Max(intersectStartVoltage, intersectEndVoltage);

                // Розширюємо межі конверта (Envelope), якщо сигнал у цій колонці вищий або нижчий за попередні
                if (segmentMinInBin < binMinVoltages[b]) binMinVoltages[b] = segmentMinInBin;
                if (segmentMaxInBin > binMaxVoltages[b]) binMaxVoltages[b] = segmentMaxInBin;

                // 7. ЧАСОВО-ЗВАЖЕНЕ СЕРЕДНЄ (Time-weighted mean):
                // Замість того, щоб просто брати середнє арифметичне всіх точок,
                // ми рахуємо площу трапеції (середня напруга відрізка * час його тривалості).
                var timeInBin = intersectEndTime - intersectStartTime;
                var averageVoltageInBin = 0.5 * (intersectStartVoltage + intersectEndVoltage);

                // Акумулюємо площу та час
                voltageTimeIntegral[b] += averageVoltageInBin * timeInBin;
                accumulatedTime[b] += timeInBin;
            }
        }

        // 8. ФІНАЛЬНИЙ РОЗРАХУНОК СЕРЕДНЬОГО ТА ПАКУВАННЯ:
        for (var b = 0; b < numBins; b++)
        {
            if (accumulatedTime[b] > 0)
            {
                // Формула: Середнє = Інтеграл (Площа) / Загальний Час
                var trueMeanVoltage = (float)(voltageTimeIntegral[b] / accumulatedTime[b]);
                destinationBins[b] = new ColumnStats(trueMeanVoltage, binMinVoltages[b], binMaxVoltages[b], alpha: 1.0f);
            }
            else
            {
                // Якщо в цю екранну колонку не потрапив жоден сигнал - робимо її порожньою
                destinationBins[b] = new ColumnStats(0, 1, 0, 0);
            }
        }
    }
}
