using Zeroconf;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System;
using System.Net.Sockets;
using System.Net;
using System.Net.NetworkInformation;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using System.Threading;
using Avalonia.Threading;

namespace RigolScopeViewer.Sources.VISA;

public partial class ZeroconfScanner
{
    public ObservableCollection<string> DiscoveredDevices { get; } = [];

    private static readonly string[] s_domains = ["_lxi._tcp.local.", "_scpi-raw._tcp.local.", "_vxi-11._tcp.local."];

    [RelayCommand]
    public async Task ScanNetworkAsync()
    {
        DiscoveredDevices.Clear();
        DiscoveredDevices.Add("Scanning network...");

        var foundDevices = new ConcurrentBag<string>();

        // Запускаємо обидва сканування паралельно
        var mdnsTask = ScanMdnsAsync(foundDevices);
        var bruteForceTask = ScanSubnetAsync(foundDevices);

        await Task.WhenAll(mdnsTask, bruteForceTask);

        DiscoveredDevices.Clear();

        // Фільтруємо дублікати (бо прилад міг відповісти і по mDNS, і по IP)
        var uniqueDevices = foundDevices.Distinct().ToList();

        foreach (var dev in uniqueDevices)
        {
            DiscoveredDevices.Add(dev);
        }

        if (DiscoveredDevices.Count == 0)
        {
            DiscoveredDevices.Add("No devices found.");
        }
    }

    private async Task ScanMdnsAsync(ConcurrentBag<string> results)
    {
        try
        {
            var hosts = await ZeroconfResolver.ResolveAsync(s_domains, scanTime: TimeSpan.FromSeconds(3));
            foreach (var host in hosts)
            {
                results.Add($"{host.IPAddress} ({host.DisplayName})");
            }
        }
        catch { /* Ігноруємо помилки mDNS */ }
    }

    private async Task ScanSubnetAsync(ConcurrentBag<string> results)
    {
        try
        {
            // 1. Магія: дізнаємося нашу IP-адресу, яка дивиться в інтернет
            using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var localIp = (socket.LocalEndPoint as IPEndPoint)?.Address;

            if (localIp == null) return;

            // 2. Отримуємо маску підмережі для цієї IP
            var mask = GetSubnetMask(localIp);
            if (mask == null) return;

            byte[] maskBytes = mask.GetAddressBytes();
            byte[] ipBytes = localIp.GetAddressBytes();

            // 3. Перевіряємо, чи підмережа не більша за /24 (255.255.255.X)
            // Якщо вона більша (наприклад /16), сканування займе вічність, тому скасовуємо
            if (maskBytes[0] != 255 || maskBytes[1] != 255 || maskBytes[2] != 255)
            {
                return;
            }

            // 4. Генеруємо всі IP адреси в цій /24 підмережі
            var ipsToScan = new List<string>();
            for (int i = 1; i < 255; i++)
            {
                if (i == ipBytes[3]) continue; // Пропускаємо свій власний комп'ютер
                ipsToScan.Add($"{ipBytes[0]}.{ipBytes[1]}.{ipBytes[2]}.{i}");
            }

            // 5. Паралельно "пінгуємо" порт 5555 на всіх 253 адресах
            // Обмежуємо паралельність, щоб не вичерпати пул сокетів ОС
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 50 };

            await Parallel.ForEachAsync(ipsToScan, parallelOptions, async (ipStr, ct) =>
            {
                await TryConnectAndIdentifyAsync(ipStr, results);
            });
        }
        catch { /* Ігноруємо мережеві помилки ОС */ }
    }

    private async Task TryConnectAndIdentifyAsync(string ipStr, ConcurrentBag<string> results)
    {
        try
        {
            using var client = new TcpClient();
            // Даємо 500мс на підключення. Якщо приладу немає, відвалиться по тайм-ауту
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

            await client.ConnectAsync(ipStr, 5555, cts.Token);

            // Якщо підключилися - запитуємо IDN, щоб переконатися, що це осцилограф
            using var stream = client.GetStream();
            var req = Encoding.ASCII.GetBytes("*IDN?\n");
            await stream.WriteAsync(req, cts.Token);

            byte[] buffer = new byte[256];
            int read = await stream.ReadAsync(buffer, cts.Token);
            if (read > 0)
            {
                string idn = Encoding.ASCII.GetString(buffer, 0, read).Trim();
                results.Add($"{ipStr} ({idn})");
            }
        }
        catch { /* Глухо - приладу немає на цій IP або порт закритий */ }
    }

    private IPAddress? GetSubnetMask(IPAddress address)
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up) continue;

            foreach (var unicastInfo in adapter.GetIPProperties().UnicastAddresses)
            {
                if (unicastInfo.Address.AddressFamily == AddressFamily.InterNetwork &&
                    unicastInfo.Address.Equals(address))
                {
                    return unicastInfo.IPv4Mask;
                }
            }
        }
        return null;
    }
}
