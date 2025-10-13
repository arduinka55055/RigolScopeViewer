using RigolScopeViewer.Models;
using System;
using System.Collections.Generic;
using Avalonia.Media;
using System.IO;
using System.Linq;
using System.Text;

namespace RigolScopeViewer.Services;

public class RigolBinLoader : IWaveformLoader
{
    public List<Waveform> Load(string fileName)
    {
        var waveforms = new List<Waveform>();
        using var stream = File.OpenRead(fileName);
        using var reader = new BinaryReader(stream);
        
        // File header (16 bytes)
        var cookie = reader.ReadBytes(2);
        if (Encoding.ASCII.GetString(cookie) != "RG")
            throw new Exception("Invalid Rigol binary file");
        
        var version = reader.ReadBytes(2);
        var fileSize = reader.ReadInt64();
        var numWaveforms = reader.ReadInt32();
        
        for (int i = 0; i < numWaveforms; i++)
        {
            // Waveform header
            int headerSize = reader.ReadInt32();
            byte[] headerData = reader.ReadBytes(headerSize - 4);
            
            using var headerReader = new BinaryReader(new MemoryStream(headerData));
            int waveType = headerReader.ReadInt32();
            int numBuffers = headerReader.ReadInt32();
            int numPoints = headerReader.ReadInt32();
            int count = headerReader.ReadInt32();
            float xDisplayRange = headerReader.ReadSingle();
            double xDisplayOrigin = headerReader.ReadDouble();
            double xIncrement = headerReader.ReadDouble();
            double xOrigin = headerReader.ReadDouble();
            int xUnits = headerReader.ReadInt32();
            int yUnits = headerReader.ReadInt32();
            byte[] dateBytes = headerReader.ReadBytes(16);
            byte[] timeBytes = headerReader.ReadBytes(16);
            byte[] modelBytes = headerReader.ReadBytes(24);
            byte[] channelNameBytes = headerReader.ReadBytes(16);
            
            string channelName = Encoding.ASCII.GetString(channelNameBytes).TrimEnd('\0');
            
            // Waveform data header
            int dataHeaderSize = reader.ReadInt32();
            short bufferType = reader.ReadInt16();
            short bytesPerPoint = reader.ReadInt16();
            long bufferSize = reader.ReadInt64();
            
            // Read waveform data
            byte[] data = reader.ReadBytes((int)bufferSize);
            
            // Create time array
            double[] timeData = new double[numPoints];
            for (int j = 0; j < numPoints; j++)
            {
                timeData[j] = xOrigin + j * xIncrement;
            }
            
            if (bufferType == 1) // Analog data
            {
                float[] floatData = new float[numPoints];
                Buffer.BlockCopy(data, 0, floatData, 0, data.Length);
                double[] analogData = floatData.Select(f => (double)f).ToArray();
                
                waveforms.Add(new Waveform
                {
                    Name = channelName,
                    Type = WaveformType.Analog,
                    TimeData = timeData,
                    AnalogData = analogData,
                    Color = GetChannelColor(i)
                });
            }
            else if (bufferType == 5) // Digital data
            {
                waveforms.Add(new Waveform
                {
                    Name = channelName,
                    Type = WaveformType.Digital,
                    TimeData = timeData,
                    DigitalData = data,
                    Color = GetChannelColor(i)
                });
            }
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
            3 => Colors.Blue,
            4 => Colors.Lime,
            5 => Colors.Orange,
            _ => Colors.White
        };
    }
}