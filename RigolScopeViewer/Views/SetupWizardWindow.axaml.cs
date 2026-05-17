using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RigolScopeViewer.Views;

public partial class SetupWizardWindow : Window
{
    public SetupWizardWindow()
    {
        InitializeComponent();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void TestButton_Click(object? sender, RoutedEventArgs e)
    {
        // Add test logic later or bind to ViewModel command
    }
}
