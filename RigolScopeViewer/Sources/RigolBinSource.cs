using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RigolScopeViewer.Interfaces;
using RigolScopeViewer.Models;
using Microsoft.Extensions.Logging;

namespace RigolScopeViewer.Sources;

public class RigolBinSource : IWaveformSource
{
    private readonly string _filePath;
    private readonly ILogger<RigolBinSource>? _logger;

    // Всі дані лежать тут. 1-й вимір - канал, 2-й вимір - точки
    private float[][]? _channelData;
    private WaveformMetadata[]? _metadata;

    public event EventHandler? DataReady;
    public int ChannelCount => _channelData?.Length ?? 0;

    public RigolBinSource(string filePath, ILogger<RigolBinSource>? logger = null)
    {
        _filePath = filePath;
        _logger = logger;
        _logger?.LogInformation("RigolBinSource initialized for file: {FilePath}", filePath);
    }

    public async Task<bool> RunSetupAsync()
    {
        // Binary files typically do not need user setup
        try
        {
            await Task.Run(ParseFile);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing bin file: {ex.Message}");
            return false;
        }
    }

    private void ParseFile()
    {
        if (!File.Exists(_filePath)) return;

        using var stream = File.OpenRead(_filePath);
        using var reader = new BinaryReader(stream);

        // File header (16 bytes)
        var cookie = reader.ReadBytes(2);
        if (Encoding.ASCII.GetString(cookie) != "RG")
            throw new Exception("Invalid Rigol binary file");

        var version = reader.ReadBytes(2);
        var fileSize = reader.ReadInt64();
        var numWaveforms = reader.ReadInt32();

        var tempChannelData = new List<float[]>();
        var tempMetadata = new List<WaveformMetadata>();

        for (var i = 0; i < numWaveforms; i++)
        {
            // Waveform header
            var headerSize = reader.ReadInt32();
            var headerData = reader.ReadBytes(headerSize - 4);

            using var headerReader = new BinaryReader(new MemoryStream(headerData));
            var waveType = headerReader.ReadInt32();
            var numBuffers = headerReader.ReadInt32();
            var numPoints = headerReader.ReadInt32();
            var count = headerReader.ReadInt32();
            var xDisplayRange = headerReader.ReadSingle();
            var xDisplayOrigin = headerReader.ReadDouble();
            var xIncrement = headerReader.ReadDouble();
            var xOrigin = headerReader.ReadDouble();
            var xUnits = headerReader.ReadInt32();
            var yUnits = headerReader.ReadInt32();
            var dateBytes = headerReader.ReadBytes(16);
            var timeBytes = headerReader.ReadBytes(16);
            var modelBytes = headerReader.ReadBytes(24);
            var channelNameBytes = headerReader.ReadBytes(16);

            var channelName = Encoding.ASCII.GetString(channelNameBytes).TrimEnd('\0');

            // Waveform data header
            var dataHeaderSize = reader.ReadInt32();
            var bufferType = reader.ReadInt16();
            var bytesPerPoint = reader.ReadInt16();
            var bufferSize = reader.ReadInt64();

            // Read waveform data
            var data = reader.ReadBytes((int)bufferSize);

            if (bufferType == 1) // Analog data
            {
                var floatData = new float[numPoints];
                Buffer.BlockCopy(data, 0, floatData, 0, data.Length);

                tempChannelData.Add(floatData);
                tempMetadata.Add(new WaveformMetadata
                {
                    ChannelName = channelName,
                    StartTime = (float)xOrigin,
                    SampleInterval = (float)xIncrement,
                    TotalPoints = numPoints
                });
            }
            else if (bufferType == 5) // Digital data
            {
                var floatData = new float[numPoints];
                for (int j = 0; j < numPoints; j++)
                {
                    floatData[j] = data[j];
                }

                tempChannelData.Add(floatData);
                tempMetadata.Add(new WaveformMetadata
                {
                    ChannelName = channelName,
                    StartTime = (float)xOrigin,
                    SampleInterval = (float)xIncrement,
                    TotalPoints = numPoints
                });
            }
        }

        _channelData = tempChannelData.ToArray();
        _metadata = tempMetadata.ToArray();
    }

    public WaveformMetadata GetMetadata(int channelIndex)
    {
        return _metadata?[channelIndex] ?? default;
    }

    public void ProcessChannelData(int channelIndex, double startTime, double endTime, DataProcessor processor)
    {
        if (_channelData == null || channelIndex < 0 || channelIndex >= ChannelCount) return;

        var meta = _metadata[channelIndex];
        var data = _channelData[channelIndex];

        // Конвертуємо час у індекси масиву
        var startIndex = (int)((startTime - meta.StartTime) / meta.SampleInterval);
        var endIndex = (int)((endTime - meta.StartTime) / meta.SampleInterval);

        startIndex = Math.Clamp(startIndex, 0, data.Length);
        endIndex = Math.Clamp(endIndex, startIndex, data.Length);

        ReadOnlySpan<float> slice;
        if (endIndex > startIndex)
        {

            slice = data.AsSpan(startIndex, endIndex - startIndex);
        }
        else
        {
            slice = data.AsSpan();
        }

        processor(slice, meta);
    }

    public void Start()
    {
        if (_channelData != null)
        {
            DataReady?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Stop() { }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _channelData = null;
        _metadata = null;
    }
}
