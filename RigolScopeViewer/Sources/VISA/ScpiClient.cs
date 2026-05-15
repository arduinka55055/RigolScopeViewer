using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RigolScopeViewer.Sources.VISA;

public class ScpiClient : IDisposable
{
    private readonly TcpClient _client;
    private NetworkStream _stream = null!;
    private readonly int _timeoutMs;

    public ScpiClient(string ipAddress, int port, int timeoutMs = 10000)
    {
        _timeoutMs = timeoutMs;
        _client = new TcpClient();
        
        var result = _client.BeginConnect(ipAddress, port, null, null);
        var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(timeoutMs));

        if (!success)
        {
            throw new Exception($"Connection to {ipAddress}:{port} timed out.");
        }

        _client.EndConnect(result);
        _stream = _client.GetStream();
        _stream.ReadTimeout = timeoutMs;
        _stream.WriteTimeout = timeoutMs;
    }

    public async Task WriteAsync(string command, CancellationToken ct = default)
    {
        var data = Encoding.ASCII.GetBytes(command + "\n");
        await _stream.WriteAsync(data, ct);
    }

    public async Task<string> QueryStringAsync(string command, CancellationToken ct = default)
    {
        await WriteAsync(command, ct);
        using var reader = new StreamReader(_stream, Encoding.ASCII, leaveOpen: true);
        return await reader.ReadLineAsync(ct) ?? string.Empty;
    }

    public async Task<byte[]> QueryBinaryValuesAsync(string command, CancellationToken ct = default)
    {
        await WriteAsync(command, ct);
        
        // Read TMC block header: #NXXXXXX...
        // 1. Read '#'
        int hash = _stream.ReadByte();
        if (hash != '#') throw new Exception("Invalid binary block header");

        // 2. Read length of length (N)
        int nBytesForLength = _stream.ReadByte() - '0';
        if (nBytesForLength < 0 || nBytesForLength > 9) throw new Exception("Invalid binary block header length");

        // 3. Read length
        byte[] lenBytes = new byte[nBytesForLength];
        int read = await _stream.ReadAsync(lenBytes, ct);
        if (read != nBytesForLength) throw new Exception("Failed to read binary block length");

        string lenStr = Encoding.ASCII.GetString(lenBytes);
        int dataLength = int.Parse(lenStr);

        // 4. Read data
        byte[] data = new byte[dataLength];
        int totalRead = 0;
        while (totalRead < dataLength)
        {
            read = await _stream.ReadAsync(data.AsMemory(totalRead, dataLength - totalRead), ct);
            if (read == 0) throw new Exception("Connection closed while reading binary block");
            totalRead += read;
        }

        // 5. Optionally read the trailing newline
        int lastByte = _stream.ReadByte();
        if (lastByte != '\n' && lastByte != -1)
        {
            // sometimes there's a trailing newline, sometimes not
        }

        return data;
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _client?.Dispose();
    }
}