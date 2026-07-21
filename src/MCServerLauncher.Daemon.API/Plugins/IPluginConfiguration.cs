using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Plugins;

/// <summary>
/// Cold-start plugin configuration reader. Reads optional same-directory
/// <c>config.json</c> once; never exposes the resolved path, never watches.
/// Configuration access is a base plugin API (not a feature).
/// </summary>
public interface IPluginConfiguration
{
    /// <summary>
    /// True when a config.json file was present at plugin startup.
    /// Distinguishes missing file from invalid JSON (the latter fails plugin start).
    /// </summary>
    bool Exists { get; }

    /// <summary>
    /// Deserializes the cold-start config snapshot using explicit source-generated metadata.
    /// Returns Err when the file is missing or when the payload cannot bind to <typeparamref name="T"/>.
    /// </summary>
    Result<T, DaemonError> Get<T>(JsonTypeInfo<T> typeInfo)
        where T : notnull;
}
