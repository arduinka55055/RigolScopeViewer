namespace RigolScopeViewer.Interfaces;

/// <summary>
/// Service for managing application configuration storage and retrieval.
/// Supports multiple backends (JSON files, SQLite, etc.)
/// </summary>
public interface IConfigManager
{
    /// <summary>
    /// Loads configuration of type T from the specified filename.
    /// Returns a new instance if the file doesn't exist or fails to load.
    /// </summary>
    T Load<T>(string filename) where T : new();

    /// <summary>
    /// Saves configuration to the specified filename.
    /// </summary>
    void Save<T>(T config, string filename);
}
