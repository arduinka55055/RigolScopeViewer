using System;
using System.Buffers;
using RigolScopeViewer.Models;

namespace RigolScopeViewer.Services;

// Цей клас відповідає за 1 кадр даних. Він IDisposable, щоб повертати пам'ять у пул.
public sealed class RenderFrame(ColumnStats[] binsArray, int validLength, ViewportState viewport) : IDisposable
{
    public ColumnStats[] BinsArray { get; } = binsArray;
    public int ValidLength { get; } = validLength;
    public ViewportState Viewport { get; } = viewport;

    // Віддаємо безпечний Span потрібного розміру (бо BinsArray може бути більшим за ValidLength)
    public ReadOnlySpan<ColumnStats> GetValidSpan() => BinsArray.AsSpan(0, ValidLength);

    public void Dispose()
    {
        if (BinsArray != null)
        {
            ArrayPool<ColumnStats>.Shared.Return(BinsArray);
        }
    }
}
