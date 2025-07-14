using RigolScopeViewer.Models;
using System.Collections.Generic;

namespace RigolScopeViewer.Services;

public interface IWaveformLoader
{
    List<Waveform> Load(string fileName);
}