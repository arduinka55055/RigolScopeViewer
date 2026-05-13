using System;
using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

public static class ConfigManager
{
    private static readonly string s_appDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Assembly.GetEntryAssembly()?.GetName().Name ?? "ScopeViewer");

    public static T Load<T>(string filename) where T : new()
    {
        var path = Path.Combine(s_appDataFolder, filename);
        if (!File.Exists(path)) return new T();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json) ?? new T();
        }
        catch { return new T(); }
    }

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    public static void Save<T>(T config, string filename)
    {
        Directory.CreateDirectory(s_appDataFolder);
        var path = Path.Combine(s_appDataFolder, filename);
        var json = JsonSerializer.Serialize(config, s_jsonOptions);
        File.WriteAllText(path, json);
    }
}
