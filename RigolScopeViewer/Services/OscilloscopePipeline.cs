using System;
using System.Buffers;
using System.Threading;
using RigolScopeViewer.Models;

namespace RigolScopeViewer.Services;

// Інтерфейс для DI
public interface IOscilloscopePipeline
{
    RenderFrame ProcessFrame(ReadOnlySpan<float> rawData, WaveformMetadata metadata, ViewportState viewport, CancellationToken cancellationToken = default);
}

public class OscilloscopePipeline(IResampler<ColumnStats> resampler) : IOscilloscopePipeline
{
    private readonly IResampler<ColumnStats> _resampler = resampler;

    public RenderFrame ProcessFrame(
        ReadOnlySpan<float> rawData, // БЕЗ float[]! Тільки Span.
        WaveformMetadata metadata,
        ViewportState viewport,
        CancellationToken cancellationToken = default)
    {
        var rentedArray = ArrayPool<ColumnStats>.Shared.Rent(viewport.ScreenWidthPx);

        try
        {
            var destinationSpan = rentedArray.AsSpan(0, viewport.ScreenWidthPx);

            // CPU рахує математику тут
            // FIXME: float vs double - треба визначитись, бо це впливає на швидкість і точність. Можливо, для часу краще double, а для вольтажу float?
            _resampler.Resample(rawData, metadata, viewport.Time, destinationSpan, cancellationToken);

            return new RenderFrame(rentedArray, viewport.ScreenWidthPx, viewport);
        }
        catch
        {
            ArrayPool<ColumnStats>.Shared.Return(rentedArray);
            throw;
        }
    }
}
