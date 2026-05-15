using Microsoft.Extensions.Logging;
using RigolScopeViewer.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using RigolScopeViewer.Models;

namespace RigolScopeViewer.Sources.VISA;

public class VisaWaveformSource : IWaveformSource
{
    private readonly string _connectionString;
    private readonly ILogger<VisaWaveformSource>? _logger;

    public event EventHandler? DataReady;

    public int ChannelCount => 1; // Stub

    public VisaWaveformSource(string connectionString, ILogger<VisaWaveformSource>? logger = null)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public Task<bool> RunSetupAsync()
    {
        _logger?.LogInformation("VisaWaveformSource setup for {ConnectionString}", _connectionString);
        return Task.FromResult(true);
    }

    public WaveformMetadata GetMetadata(int channelIndex)
    {
        return new WaveformMetadata
        {
            ChannelName = $"VISA CH{channelIndex + 1}",
            SampleInterval = 1e-6f,
            StartTime = 0,
            TotalPoints = 1000
        };
    }

    public void ProcessChannelData(int channelIndex, TimeRange timeRange, DataProcessor processor, CancellationToken cancellationToken = default)
    {
        // Stub
        Span<float> fakeData = new float[1000];
        processor(fakeData, GetMetadata(channelIndex), cancellationToken);
    }

    public void Start()
    {
        DataReady?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}
