using System;
using System.IO.MemoryMappedFiles;

public class SharedMemoryReader : IDisposable
{
    private MemoryMappedFile _mmf;
    private MemoryMappedViewAccessor _accessor;

    /*
        Linux: 
        // Source - https://stackoverflow.com/a/77149446
        // Posted by user1576055, modified by community. See post 'Timeline' for change history
        // Retrieved 2026-04-20, License - CC BY-SA 4.0
        MemoryMappedFile.CreateFromFile("/dev/shm/test", System.IO.FileMode.OpenOrCreate, null, 100000);
    */
    public SharedMemoryReader(string mapName, long capacity)
    {
        _mmf = MemoryMappedFile.OpenExisting(mapName);
        _accessor = _mmf.CreateViewAccessor(0, capacity, MemoryMappedFileAccess.Read);
    }

    // Оголошуємо делегат, який може приймати ref struct (ReadOnlySpan)
    public delegate void DataProcessor(ReadOnlySpan<float> rawData);

    /// <summary>
    /// Безпечно інкапсулює unsafe код. Гарантує звільнення вказівника ОС.
    /// </summary>
    /* 
    public unsafe void ProcessCurrentData(int pointsCount, DataProcessor processor)
    {
        byte* pointer = null;
        try
        {
            // Блокуємо пам'ять ОС
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            
            // Створюємо Span. (pointer автоматично приводиться до void*).
            // pointsCount - це кількість float, конструктор сам помножить на 4 байти.
            var span = new ReadOnlySpan<float>(pointer, pointsCount);
            
            // Віддаємо Span у твій безпечний пайплайн (наприклад, IResampler)
            processor(span); 
        }
        finally
        {
            // ГАРАНТОВАНО звільняємо пам'ять, навіть якщо processor викине Exception
            if (pointer != null)
            {
                _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
    }
    */
    public void Dispose()
    {
        _accessor?.Dispose();
        _mmf?.Dispose();
    }
}