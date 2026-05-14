using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using RigolScopeViewer.Models;
using RigolScopeViewer.Services;
using RigolScopeViewer.ViewModels;
// Якщо у тебе математика в окремій папці:
// using RigolScopeViewer.Services.Samplers; 

namespace RigolScopeViewer;

public partial class App : Application
{
    // Глобальний провайдер сервісів (корисно, якщо десь треба витягнути сервіс вручну)
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Видаляємо валідацію Avalonia, щоб уникнути конфліктів з CommunityToolkit.Mvvm
        BindingPlugins.DataValidators.RemoveAt(0);

        // 1. НАЛАШТОВУЄМО DI КОНТЕЙНЕР
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        Services = serviceCollection.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 2. ІНІЦІАЛІЗАЦІЯ ПЛАГІНІВ TODO: (Завантажуємо DLL перед запуском вікна)
            //var pluginManager = Services.GetRequiredService<PluginManager>();
            //string pluginsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
            //pluginManager.LoadExternalPlugins(pluginsPath);

            // 3. ЗАПУСК ГОЛОВНОГО ВІКНА
            desktop.MainWindow = new MainWindow(Services.GetRequiredService<MainViewModel>());
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Тут ми реєструємо всі класи, які існують у нашій програмі
    private void ConfigureServices(IServiceCollection services)
    {
        services.AddRigolScopeViewerServices();
        services.AddTransient<MainViewModel>();

    }
}
