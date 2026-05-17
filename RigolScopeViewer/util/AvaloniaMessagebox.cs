using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.Input;
using RigolScopeViewer;
using RigolScopeViewer.Interfaces;

namespace RigolScopeViewer.Util;

public class AvaloniaMessageBox : IAlertModal
{

    public async void Show(string title, string message)
    {
        var appLifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        var mainWindow = appLifetime?.MainWindow;

        var msgBox = new Window
        {
            Title = title,
            Width = 300,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        msgBox.Content = new StackPanel
        {
            Margin = new Thickness(10),
            Children =
            {
                new TextBlock { Text = message, Margin = new Thickness(0, 0, 0, 10) },
                new Button
                {
                    Content = "OK",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Command = new RelayCommand(() => msgBox.Close())
                }
            }
        };

        if (mainWindow != null)
        {
            await msgBox.ShowDialog(mainWindow);
        }
        else
        {
            msgBox.Show();
        }
    }
}
