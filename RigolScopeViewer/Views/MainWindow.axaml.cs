using Avalonia;
using Avalonia.Controls;
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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainViewModel vm)
        {
            // Subscribe to property changes to update the display
            vm.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.TimePerDivision) ||
                    args.PropertyName == nameof(MainViewModel.TimeOffset) ||
                    args.PropertyName == nameof(MainViewModel.TriggerLevel) ||
                    args.PropertyName == nameof(MainViewModel.ShowTrigger))
                {
                    // Find the oscilloscope control and update it
                    var scopeControl = this.FindControl<OscilloscopeControl>("OscilloscopeView");
                }
            };
        }
    }
}
