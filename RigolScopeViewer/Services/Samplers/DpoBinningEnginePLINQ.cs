using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using RigolScopeViewer.Models;

namespace RigolScopeViewer.Services.Samplers;

public class DpoBinningEnginePLINQ : IResampler<ColumnStats>
{
    private readonly ILogger<DpoBinningEngine>? _logger;

    public DpoBinningEnginePLINQ(ILogger<DpoBinningEngine>? logger = null)
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
        int numBins = destinationBins.Length;
        if (numBins == 0 || sourceVoltages.Length < 2)
        {
            destinationBins.Fill(new ColumnStats(0, 1, 0, 0));
            return;
        }

        double startTime = metadata.StartTime;
        double sampleInterval = metadata.SampleInterval;
        double timeStart = timeRange.Start;
        double timeEnd = timeRange.End;
        double duration = timeEnd - timeStart;

        if (duration <= 0.0)
        {
            destinationBins.Fill(new ColumnStats(0, 1, 0, 0));
            return;
        }

        double binWidth = duration / numBins;
        float[] voltagesArray = sourceVoltages.ToArray();
        int segmentCount = sourceVoltages.Length - 1;

        // PLINQ aggregation using the three-parameter overload:
        // seedFactory, updateAccumulatorFunc, combineAccumulatorsFunc
        var finalAcc = Enumerable.Range(0, segmentCount)
            .AsParallel()
            .WithCancellation(cancellationToken)
            .Aggregate(
                // Thread-local seed: fresh accumulator for each thread
                seedFactory: () =>
                {
                    var mins = new float[numBins];
                    var maxs = new float[numBins];
                    var sum = new double[numBins];
                    var totalDt = new double[numBins];
                    Array.Fill(mins, float.MaxValue);
                    Array.Fill(maxs, float.MinValue);
                    return (mins, maxs, sum, totalDt);
                },
                // Update accumulator with one segment
                updateAccumulatorFunc: (acc, i) =>
                {
                    double tA = startTime + i * sampleInterval;
                    double tB = startTime + (i + 1) * sampleInterval;
                    float vA = voltagesArray[i];
                    float vB = voltagesArray[i + 1];

                    double tMin = Math.Min(tA, tB);
                    double tMax = Math.Max(tA, tB);
                    float vMin = tA <= tB ? vA : vB;
                    float vMax = tA <= tB ? vB : vA;

                    if (tMax < timeStart || tMin > timeEnd)
                        return acc;

                    double t0 = Math.Max(tMin, timeStart);
                    double t1 = Math.Min(tMax, timeEnd);
                    if (t1 <= t0)
                        return acc;

                    double frac0 = (t0 - tMin) / (tMax - tMin + 1e-300);
                    double frac1 = (t1 - tMin) / (tMax - tMin + 1e-300);
                    float v0 = (float)(vMin + (vMax - vMin) * frac0);
                    float v1 = (float)(vMin + (vMax - vMin) * frac1);

                    double bin0 = (t0 - timeStart) / binWidth;
                    double bin1 = (t1 - timeStart) / binWidth;
                    int binStart = Math.Clamp((int)Math.Floor(bin0), 0, numBins - 1);
                    int binEnd = Math.Clamp((int)Math.Floor(bin1), 0, numBins - 1);

                    for (int b = binStart; b <= binEnd; b++)
                    {
                        double binLeft = timeStart + b * binWidth;
                        double binRight = binLeft + binWidth;

                        double tIn = Math.Max(t0, binLeft);
                        double tOut = Math.Min(t1, binRight);
                        if (tIn >= tOut) continue;

                        double uIn = (tIn - t0) / (t1 - t0 + 1e-300);
                        double uOut = (tOut - t0) / (t1 - t0 + 1e-300);
                        float vIn = (float)(v0 + (v1 - v0) * uIn);
                        float vOut = (float)(v0 + (v1 - v0) * uOut);

                        float vLow = Math.Min(vIn, vOut);
                        float vHigh = Math.Max(vIn, vOut);
                        if (vLow < acc.mins[b]) acc.mins[b] = vLow;
                        if (vHigh > acc.maxs[b]) acc.maxs[b] = vHigh;

                        double dt = tOut - tIn;
                        double avgV = 0.5 * (vIn + vOut);
                        acc.sum[b] += avgV * dt;
                        acc.totalDt[b] += dt;
                    }
                    return acc;
                },
                // Combine two thread-local accumulators
                combineAccumulatorsFunc: (acc1, acc2) =>
                {
                    for (int b = 0; b < numBins; b++)
                    {
                        if (acc2.mins[b] < acc1.mins[b]) acc1.mins[b] = acc2.mins[b];
                        if (acc2.maxs[b] > acc1.maxs[b]) acc1.maxs[b] = acc2.maxs[b];
                        acc1.sum[b] += acc2.sum[b];
                        acc1.totalDt[b] += acc2.totalDt[b];
                    }
                    return acc1;
                },
                resultSelector: acc => acc   // <-- added this line
            );

        // Fill destination bins from the merged accumulator
        for (int b = 0; b < numBins; b++)
        {
            if (finalAcc.totalDt[b] > 0)
            {
                float mean = (float)(finalAcc.sum[b] / finalAcc.totalDt[b]);
                destinationBins[b] = new ColumnStats(mean, finalAcc.mins[b], finalAcc.maxs[b], alpha: 1.0f);
            }
            else
            {
                destinationBins[b] = new ColumnStats(0, 1, 0, 0);
            }
        }

        //fake delay to test UI
        //Thread.Sleep(2000);
    }
}
