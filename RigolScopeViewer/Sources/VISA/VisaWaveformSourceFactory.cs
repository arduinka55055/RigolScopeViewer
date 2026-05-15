using Microsoft.Extensions.Logging;
using RigolScopeViewer.Interfaces;
using System;

namespace RigolScopeViewer.Sources.VISA;

public class VisaWaveformSourceFactory : IWaveformSourceFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public string Name => "VISA Network/USB Source";
    public string[] SupportedExtensions => Array.Empty<string>();
    public bool IsCaptureSource => true;

    public VisaWaveformSourceFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public IWaveformSource CreateSource(string connectionStringOrFilePath)
    {
        return new VisaWaveformSource(connectionStringOrFilePath, _loggerFactory.CreateLogger<VisaWaveformSource>());
    }
}
