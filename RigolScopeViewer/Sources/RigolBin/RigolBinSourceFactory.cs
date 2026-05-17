using Microsoft.Extensions.Logging;
using RigolScopeViewer.Interfaces;
using System;

namespace RigolScopeViewer.Sources.RigolBin;

public class RigolBinSourceFactory : IWaveformSourceFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public string Name => "Rigol Binary File";
    public string[] SupportedExtensions => new[] { ".bin" };
    public bool IsCaptureSource => false;

    public RigolBinSourceFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public IWaveformSource CreateSource(string connectionStringOrFilePath)
    {
        return new RigolBinSource(connectionStringOrFilePath, _loggerFactory.CreateLogger<RigolBinSource>());
    }
}
