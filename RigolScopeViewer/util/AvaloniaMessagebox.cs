using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.Input;


public static class AvaloniaMessageBox
{
    public static void ShowCustomMessageBox(string title, string message)
    {
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

        msgBox.Show();
    }
}