using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using RigolScopeViewer.Models;
using ComputeSharp;

namespace RigolScopeViewer.Services.Samplers;

public class DpoBinningEngineGPU : IResampler<ColumnStats>, IDisposable
{
    private readonly ILogger<DpoBinningEngineGPU>? _logger;

    // Кешуємо буфери відеопам'яті для підтримки Zero-Allocation
    private ReadOnlyBuffer<float>? _gpuSourceBuffer;
    private ReadWriteBuffer<Float4>? _gpuDestBuffer;
    private int _cachedSourceCapacity = 0;
    private int _cachedNumBins = 0;

    public DpoBinningEngineGPU(ILogger<DpoBinningEngineGPU>? logger = null)
    {
        _logger = logger;
        _logger?.LogDebug("DpoBinningEngineGPU initialized");
    }

    public void Resample(
        ReadOnlySpan<float> sourceVoltages,
        in WaveformMetadata metadata,
        TimeRange timeRange,
        Span<ColumnStats> destinationBins,
        CancellationToken cancellationToken = default)
    {
        int numBins = destinationBins.Length;
        int sourceLength = sourceVoltages.Length;

        double duration = timeRange.Duration;
        if (numBins == 0 || sourceLength < 2 || duration <= 0.0)
        {
            destinationBins.Fill(new ColumnStats(0, 1, 0, 0));
            return;
        }

        // 1. КЕРУВАННЯ ПАМ'ЯТТЮ ВІДЕОКАРТИ (Zero-Allocation)
        var device = GraphicsDevice.GetDefault();

        if (_gpuSourceBuffer == null || _cachedSourceCapacity < sourceLength)
        {
            _gpuSourceBuffer?.Dispose();
            _gpuSourceBuffer = device.AllocateReadOnlyBuffer<float>(sourceLength);
            _cachedSourceCapacity = sourceLength;
        }

        if (_gpuDestBuffer == null || _cachedNumBins != numBins)
        {
            _gpuDestBuffer?.Dispose();
            _gpuDestBuffer = device.AllocateReadWriteBuffer<Float4>(numBins);
            _cachedNumBins = numBins;
        }

        _gpuSourceBuffer.CopyFrom(sourceVoltages);

        // 2. МАТЕМАТИКА ПРОСТОРУ ЕКРАНУ (Високоточний розрахунок на CPU)
        double binWidthTime = duration / numBins;

        // Знаходимо точний індекс масиву, який відповідає лівому краю екрану
        double exactStartIndex = (timeRange.Start - metadata.StartTime) / metadata.SampleInterval;

        // Розбиваємо його на цілу і дробову частину, щоб не втратити точність float на GPU
        int indexOffset = (int)Math.Floor(exactStartIndex);
        float subIndexOffset = (float)(exactStartIndex - indexOffset);

        // Співвідношення кроку дискретизації до ширини пікселя екрану
        float scaleX = (float)(metadata.SampleInterval / binWidthTime);

        // 3. КОНФІГУРАЦІЯ ТА ЗАПУСК ШЕЙДЕРА
        var shader = new DpoBinningShader(
            _gpuSourceBuffer,
            _gpuDestBuffer,
            indexOffset,
            subIndexOffset,
            scaleX,
            sourceLength,
            numBins
        );

        // Диспетчеризація 1 потоку на 1 піксель екрану
        device.For(numBins, shader);

        // 4. ТРАНСФЕР РЕЗУЛЬТАТІВ БЕЗ КОПІЮВАННЯ (MemoryMarshal Cast)
        var destFloat4Span = MemoryMarshal.Cast<ColumnStats, Float4>(destinationBins);

        // GPU видає Float4 (16 байт), а нам треба ColumnStats (8 байт)
        var gpuData = new Float4[numBins];
        _gpuDestBuffer.CopyTo(gpuData);

        for (int i = 0; i < numBins; i++)
        {
            destinationBins[i] = new ColumnStats(gpuData[i].X, gpuData[i].Y, gpuData[i].Z, gpuData[i].W);
        }
    }

    public void Dispose()
    {
        _gpuSourceBuffer?.Dispose();
        _gpuDestBuffer?.Dispose();
        GC.SuppressFinalize(this);
    }
}

// -------------------------------------------------------------------------------------
// СУТНІСТЬ ШЕЙДЕРА (Має бути в тому ж файлі або поруч)
// -------------------------------------------------------------------------------------




[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct DpoBinningShader : IComputeShader
{
    public readonly ReadOnlyBuffer<float> SourceVoltages;
    public readonly ReadWriteBuffer<Float4> DestinationBins;

    public readonly int IndexOffset;
    public readonly float SubIndexOffset;
    public readonly float ScaleX;
    public readonly int SourceLength;
    public readonly int NumBins;

    public DpoBinningShader(
        ReadOnlyBuffer<float> sourceVoltages,
        ReadWriteBuffer<Float4> destinationBins,
        int indexOffset,
        float subIndexOffset,
        float scaleX,
        int sourceLength,
        int numBins)
    {
        SourceVoltages = sourceVoltages;
        DestinationBins = destinationBins;
        IndexOffset = indexOffset;
        SubIndexOffset = subIndexOffset;
        ScaleX = scaleX;
        SourceLength = sourceLength;
        NumBins = numBins;
    }

    public void Execute()
    {
        int b = ThreadIds.X;
        if (b >= NumBins) return;

        // Межі колонки в пікселях (наприклад, 0.0 до 1.0)
        float binLeft = b;
        float binRight = b + 1.0f;

        float binMin = 3.402823466e+38f;
        float binMax = -3.402823466e+38f;
        float sumVoltageDt = 0.0f;
        float totalDt = 0.0f;

        // Зворотний розрахунок індексів: знаходимо, які точки потрапляють у цей піксель
        float exactStartI = binLeft / ScaleX + SubIndexOffset + IndexOffset;
        float exactEndI = binRight / ScaleX + SubIndexOffset + IndexOffset;

        int iStart = Hlsl.Max(0, (int)Hlsl.Floor(exactStartI) - 1);
        int iEnd = Hlsl.Min(SourceLength - 2, (int)Hlsl.Ceil(exactEndI));

        // Ітерація по знайденому зрізу
        for (int i = iStart; i <= iEnd; i++)
        {
            // Трансформація індексу у координату пікселя на екрані (Screen X)
            float relativeI_A = (i - IndexOffset) - SubIndexOffset;
            float relativeI_B = (i + 1 - IndexOffset) - SubIndexOffset;

            float xA = relativeI_A * ScaleX;
            float xB = relativeI_B * ScaleX;

            float xMin = Hlsl.Min(xA, xB);
            float xMax = Hlsl.Max(xA, xB);
            float vMin = xA <= xB ? SourceVoltages[i] : SourceVoltages[i + 1];
            float vMax = xA <= xB ? SourceVoltages[i + 1] : SourceVoltages[i];

            // Відсікання сегмента по межах пікселя
            float xIn = Hlsl.Max(xMin, binLeft);
            float xOut = Hlsl.Min(xMax, binRight);

            if (xIn >= xOut) continue;

            // Лінійна інтерполяція (Lerp)
            float uIn = (xIn - xMin) / Hlsl.Max(xMax - xMin, 1e-10f);
            float uOut = (xOut - xMin) / Hlsl.Max(xMax - xMin, 1e-10f);

            float vIn = vMin + (vMax - vMin) * uIn;
            float vOut = vMin + (vMax - vMin) * uOut;

            float sMin = Hlsl.Min(vIn, vOut);
            float sMax = Hlsl.Max(vIn, vOut);
            binMin = Hlsl.Min(binMin, sMin);
            binMax = Hlsl.Max(binMax, sMax);

            // Акумуляція: dt тепер виражається у частках пікселя, що ідеально для Mean
            float dt = xOut - xIn;
            float avgV = 0.5f * (vIn + vOut);

            sumVoltageDt += avgV * dt;
            totalDt += dt;
        }

        if (totalDt > 0.0f)
        {
            float mean = sumVoltageDt / totalDt;
            DestinationBins[b] = new Float4(mean, binMin, binMax, 1.0f);
        }
        else
        {
            // Порожня колонка
            DestinationBins[b] = new Float4(0.0f, 1.0f, 0.0f, 0.0f);
        }
    }
}
