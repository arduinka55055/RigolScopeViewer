using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RigolScopeViewer.Views;

public partial class SetupWizardWindow : Window
{
    // bindable Test command
    public ICommand? TestCommand;


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
        if (TestCommand != null && TestCommand.CanExecute(null))
        {
            TestCommand.Execute(null);
        }
    }
}
