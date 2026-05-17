using Microsoft.Extensions.Logging;
using RigolScopeViewer.Interfaces;
using System;

namespace RigolScopeViewer.Sources.CSV;

public class CsvWaveformSourceFactory : IWaveformSourceFactory
{
    private readonly IConfigManager _configManager;
    private readonly ILoggerFactory _loggerFactory;

    public string Name => "CSV Data File";
    public string[] SupportedExtensions => new[] { ".csv" };
    public bool IsCaptureSource => false;

    public CsvWaveformSourceFactory(IConfigManager configManager, ILoggerFactory loggerFactory)
    {
        _configManager = configManager;
        _loggerFactory = loggerFactory;
    }

    public IWaveformSource CreateSource(string connectionStringOrFilePath)
    {
        return new CsvWaveformSource(connectionStringOrFilePath, _configManager, _loggerFactory.CreateLogger<CsvWaveformSource>());
    }
}
