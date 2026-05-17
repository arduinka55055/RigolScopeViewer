using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Data;
using System;

namespace RigolScopeViewer.Sources.VISA;

public static class PreviewerFactory
{
    public static Control CreateZeroconfPreviewer(ZeroconfScanner scanner, Action<string>? onDeviceSelected)
    {
        var stackPanel = new StackPanel
        {
            Spacing = 10,
            Margin = new Avalonia.Thickness(10)
        };

        var scanButton = new Button
        {
            Content = "Scan Network (mDNS/LXI)",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Command = scanner.ScanNetworkCommand
        };

        var listBox = new ListBox
        {
            Height = 200,
            ItemsSource = scanner.DiscoveredDevices,
            Background = new SolidColorBrush(Color.Parse("#1e1e1e")),
            BorderThickness = new Avalonia.Thickness(1),
            BorderBrush = Brushes.Gray
        };

        listBox.SelectionChanged += (s, e) =>
        {
            if (listBox.SelectedItem is string selected && !selected.Contains("Scanning") && !selected.Contains("No devices"))
            {
                // Extract IP from "192.168.1.10 (Rigol DS1000)"
                var ip = selected.Split(' ')[0];
                onDeviceSelected?.Invoke(ip);
            }
        };

        stackPanel.Children.Add(new TextBlock { Text = "Network Discovery", FontWeight = FontWeight.Bold });
        stackPanel.Children.Add(scanButton);
        stackPanel.Children.Add(listBox);
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Select a device above to auto-fill the IP address.",
            FontSize = 11,
            FontStyle = FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap
        });

        return stackPanel;
    }
}
