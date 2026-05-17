using System;
using System.Reflection;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RigolScopeViewer.Interfaces;

namespace RigolScopeViewer.Services;

/// <summary>
/// Default implementation of IConfigManager using JSON file storage.
/// </summary>
public class ConfigManager : IConfigManager
{
    private readonly string _appDataFolder;
    private readonly ILogger<ConfigManager> _logger;
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    public ConfigManager(ILogger<ConfigManager> logger)
    {
        _logger = logger;
        _appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Assembly.GetEntryAssembly()?.GetName().Name ?? "ScopeViewer");

        _logger.LogInformation("ConfigManager initialized with app data folder: {AppDataFolder}", _appDataFolder);
    }

    public T Load<T>(string filename) where T : new()
    {
        var path = Path.Combine(_appDataFolder, filename);

        if (!File.Exists(path))
        {
            _logger.LogDebug("Config file not found: {Path}, returning new instance", path);
            return new T();
        }

        try
        {
            _logger.LogDebug("Loading config from: {Path}", path);
            var json = File.ReadAllText(path);
            var result = JsonSerializer.Deserialize<T>(json) ?? new T();
            _logger.LogInformation("Successfully loaded config from {Path}", path);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load config from {Path}, returning new instance", path);
            return new T();
        }
    }

    public void Save<T>(T config, string filename)
    {
        try
        {
            Directory.CreateDirectory(_appDataFolder);
            var path = Path.Combine(_appDataFolder, filename);
            var json = JsonSerializer.Serialize(config, s_jsonOptions);
            File.WriteAllText(path, json);
            _logger.LogInformation("Config saved successfully to {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save config to {Path}", Path.Combine(_appDataFolder, filename));
            throw;
        }
    }
}
