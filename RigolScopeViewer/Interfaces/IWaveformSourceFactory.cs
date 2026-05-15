using System.Collections.Generic;

namespace RigolScopeViewer.Interfaces;

public interface IWaveformSourceFactory
{
    string Name { get; }
    string[] SupportedExtensions { get; }
    bool IsCaptureSource { get; }
    IWaveformSource CreateSource(string connectionStringOrFilePath);
}
