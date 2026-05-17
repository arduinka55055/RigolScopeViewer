using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RigolScopeViewer.ViewModels;
using System;

namespace RigolScopeViewer;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif

        DataContext = viewModel;
    }

    private void Channel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is ChannelViewModel clickedChannel)
        {
            if (DataContext is MainViewModel vm)
            {
                foreach (var ch in vm.Channels)
                {
                    ch.IsActive = (ch == clickedChannel);
                }
            }
        }
    }
}
