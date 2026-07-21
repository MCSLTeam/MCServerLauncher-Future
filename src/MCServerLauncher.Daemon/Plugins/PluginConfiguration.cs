using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

namespace MCServerLauncher.Daemon.Plugins;

/// <summary>
/// Cold-start config reader. Loads optional same-directory config.json once at construction.
/// Invalid JSON fails plugin load; missing file is distinguishable via <see cref="Exists"/>.
/// Never exposes the resolved path.
/// </summary>
internal sealed class PluginConfiguration : IPluginConfiguration
{
    private readonly byte[]? _bytes;
    private readonly PluginErrorFactory _errors;

    internal PluginConfiguration(string bundleDirectory, PluginErrorFactory errors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleDirectory);
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        var path = Path.Combine(bundleDirectory, "config.json");
        if (!File.Exists(path))
        {
            Exists = false;
            _bytes = null;
            return;
        }

        Exists = true;
        _bytes = File.ReadAllBytes(path);
        // Validate JSON well-formedness eagerly so invalid config fails at load, not first Get.
        try
        {
            using var _ = JsonDocument.Parse(_bytes);
        }
        catch (JsonException exception)
        {
            throw new PluginManifestException(
                "plugin_config_invalid",
                "The plugin config.json is not valid JSON.",
                exception);
        }
    }

    public bool Exists { get; }

    public Result<T, DaemonError> Get<T>(JsonTypeInfo<T> typeInfo)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        if (!Exists || _bytes is null)
        {
            return Result.Err<T, DaemonError>(_errors.Create(
                "plugin_config_missing",
                "The plugin config.json file is not present."));
        }

        try
        {
            var value = JsonSerializer.Deserialize(_bytes, typeInfo);
            if (value is null)
            {
                return Result.Err<T, DaemonError>(_errors.Create(
                    "plugin_config_invalid",
                    "The plugin config.json deserialized to null."));
            }

            return Result.Ok<T, DaemonError>(value);
        }
        catch (JsonException exception)
        {
            return Result.Err<T, DaemonError>(_errors.Create(
                "plugin_config_invalid",
                $"The plugin config.json could not be bound: {exception.Message}"));
        }
    }
}
