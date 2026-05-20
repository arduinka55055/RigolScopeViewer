using System;
using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using RigolScopeViewer.Models;
using RigolScopeViewer.Services.Samplers;

namespace RigolScopeViewer.Benchmarks;

[MemoryDiagnoser]
public class BinningBenchmark
{
    private float[] _sourceVoltages;
    private ColumnStats[] _destinationBins;
    private WaveformMetadata _metadata;
    private TimeRange _timeRange;

    // Оголошуємо обидва рушії
    private DpoBinningEngine _sequentialEngine;
    private DpoBinningEnginePLINQ _plinqEngine;
    private DpoBinningEngineGPU _computeEngine;

    [Params(100_000, 1_000_000, 10_000_000)]
    public int PointCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Ініціалізуємо обидва рушії (без логерів)
        _sequentialEngine = new DpoBinningEngine(null);
        _plinqEngine = new DpoBinningEnginePLINQ(null);
        _computeEngine = new DpoBinningEngineGPU();

        _sourceVoltages = new float[PointCount];
        var rand = new Random(42);
        for (int i = 0; i < PointCount; i++)
        {
            _sourceVoltages[i] = (float)(rand.NextDouble() * 5.0 - 2.5);
        }

        _destinationBins = new ColumnStats[1920];

        _metadata = new WaveformMetadata
        {
            StartTime = 0,
            SampleInterval = 1e-9f,
            TotalPoints = PointCount
        };

        _timeRange = new TimeRange(0, PointCount * 1e-9);
    }

    // Базовий метод (Baseline = true)
    [Benchmark(Baseline = true)]
    public void CpuSequentialBinning()
    {
        _sequentialEngine.Resample(
            _sourceVoltages.AsSpan(),
            _metadata,
            _timeRange,
            _destinationBins.AsSpan(),
            default);
    }

    // Додаємо метод для багатопотокового тестування
    [Benchmark]
    public void CpuPlinqBinning()
    {
        _plinqEngine.Resample(
            _sourceVoltages.AsSpan(),
            _metadata,
            _timeRange,
            _destinationBins.AsSpan(),
            default);
    }


    [Benchmark]
    public void GpuComputeBinning()
    {
        _computeEngine.Resample(
            _sourceVoltages.AsSpan(),
            _metadata,
            _timeRange,
            _destinationBins.AsSpan(),
            default);
    }

}

public class Program
{
    public static void Main(string[] args)
    {
        // Запуск конвеєра тестування
        var summary = BenchmarkRunner.Run<BinningBenchmark>();
    }
}
