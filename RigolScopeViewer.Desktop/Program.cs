using System;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using RigolScopeViewer.Services;

namespace RigolScopeViewer.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Setup dependency injection
        var services = new ServiceCollection();
        services.AddRigolScopeViewerServices();
        var serviceProvider = services.BuildServiceProvider();

        // Set the service provider on the App before building
        RigolScopeViewer.App.ServiceProvider = serviceProvider;

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<RigolScopeViewer.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

}

