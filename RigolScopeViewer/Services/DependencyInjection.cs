using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RigolScopeViewer.Interfaces;
using RigolScopeViewer.Services.Samplers;
using RigolScopeViewer.ViewModels;

namespace RigolScopeViewer.Services;

/// <summary>
/// Extension methods for configuring dependency injection services.
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Adds RigolScopeViewer services to the DI container.
    /// </summary>
    public static IServiceCollection AddRigolScopeViewerServices(
        this IServiceCollection services,
        Action<ILoggingBuilder>? configureLogging = null)
    {
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
            configureLogging?.Invoke(builder);
        });

        // Add configuration management
        services.AddSingleton<IConfigManager, ConfigManager>();

        // Add resampling/binning engine
        services.AddSingleton<IResampler<ColumnStats>, DpoBinningEngine>();

        // Реєструємо пайплайн
        services.AddTransient<IOscilloscopePipeline, OscilloscopePipeline>();

        return services;
    }
}
