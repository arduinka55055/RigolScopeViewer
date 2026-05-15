namespace RigolScopeViewer.Models;

public class VisaConfig
{
    public string IpAddress { get; set; } = "192.168.0.162";
    public int Port { get; set; } = 5555; // Rigol LXI default port
    public int TimeoutMs { get; set; } = 10000;
}
