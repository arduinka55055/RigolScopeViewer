using RigolScopeViewer.Models;
using System;
using System.Collections.Generic;
using Avalonia.Media;
using System.IO;
using System.Linq;

namespace RigolScopeViewer.Services;

public class CsvLoader : IWaveformLoader
{
    public List<Waveform> Load(string fileName)
    {
        var waveforms = new List<Waveform>();
        var lines = File.ReadAllLines(fileName);

        if (lines.Length < 2) return waveforms;

        // Parse header
        var headers = lines[0].Split(',').Select(h => h.Trim()).ToArray();
        int numChannels = headers.Length - 1; // first column is time

        // Parse data
        int numPoints = lines.Length - 1;
        double[] timeData = new double[numPoints];
        double[][] channelData = new double[numChannels][];

        for (int i = 0; i < numChannels; i++)
            channelData[i] = new double[numPoints];

        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split(',')
                .Select(v => double.TryParse(v, out double result) ? result : 0)
                .ToArray();

            if (values.Length < headers.Length)
                continue;

            timeData[i - 1] = values[0];
            for (int j = 0; j < numChannels; j++)
            {
                channelData[j][i - 1] = values[j + 1];
            }
        }

        // Create waveforms
        for (int i = 0; i < numChannels; i++)
        {
            waveforms.Add(new Waveform
            {
                Name = headers[i + 1],
                Type = WaveformType.Analog,
                TimeData = timeData,
                AnalogData = channelData[i],
                Color = GetChannelColor(i)
            });
        }

        return waveforms;
    }

    private Color GetChannelColor(int index)
    {
        return index switch
        {
            0 => Colors.Yellow,
            1 => Colors.Cyan,
            2 => Colors.Magenta,
            3 => Colors.Lime,
            _ => Colors.White
        };
    }
}