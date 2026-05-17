using Microsoft.Extensions.Logging;
using RigolScopeViewer.Interfaces;
using System;

namespace RigolScopeViewer.Sources.VISA;

public class VisaWaveformSourceFactory : IWaveformSourceFactory
{
    private readonly IConfigManager _configManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IAlertModal _alertModal;

    public string Name => "VISA Network/USB Source";
    public string[] SupportedExtensions => Array.Empty<string>();
    public bool IsCaptureSource => true;

    public VisaWaveformSourceFactory(IConfigManager configManager, ILoggerFactory loggerFactory, IAlertModal alertModal)
    {
        _configManager = configManager;
        _loggerFactory = loggerFactory;
        _alertModal = alertModal;
    }

    public IWaveformSource CreateSource(string connectionStringOrFilePath)
    {
        return new VisaWaveformSource(_configManager, _loggerFactory.CreateLogger<VisaWaveformSource>(), _alertModal);
    }
}
