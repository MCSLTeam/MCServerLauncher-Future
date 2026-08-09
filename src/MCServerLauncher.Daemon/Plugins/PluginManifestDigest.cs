using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MCServerLauncher.Daemon.API.Plugins;

namespace MCServerLauncher.Daemon.Plugins;

internal static class PluginManifestDigest
{
    private const string DigestDomain = "mcsl-plugin-manifest-v2";

    internal static string Compute(
        string packageId,
        string packageVersion,
        string entryAssembly,
        string entryType,
        string apiRange,
        IReadOnlyList<string> features,
        IReadOnlyList<PluginManifestPluginDependency>? pluginDependencies = null,
        IReadOnlyList<PluginManifestContractDependency>? contractDependencies = null,
        IReadOnlyList<PluginManifestExtensionPoint>? extensionPoints = null,
        IReadOnlyList<PluginManifestCommand>? commands = null)
    {
        var builder = new StringBuilder();
        Append(builder, DigestDomain);
        Append(builder, packageId);
        Append(builder, packageVersion);
        Append(builder, entryAssembly);
        Append(builder, entryType);
        Append(builder, apiRange);
        Append(builder, features.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var feature in features)
            Append(builder, feature);

        if (pluginDependencies is { Count: > 0 })
        {
            Append(builder, "dependencies.plugins");
            Append(builder, pluginDependencies.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var dependency in pluginDependencies
                         .OrderBy(static item => item.Id, StringComparer.Ordinal))
            {
                Append(builder, dependency.Id);
                Append(builder, dependency.NormalizedVersionRange);
            }
        }

        if (contractDependencies is { Count: > 0 })
        {
            Append(builder, "dependencies.contracts");
            Append(builder, contractDependencies.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var dependency in contractDependencies
                         .OrderBy(static item => item.AssemblyName, StringComparer.Ordinal))
            {
                Append(builder, dependency.Assembly);
                Append(builder, dependency.NormalizedVersionRange);
                Append(builder, dependency.Sha256);
            }
        }

        if (extensionPoints is { Count: > 0 })
        {
            Append(builder, "extensionPoints");
            Append(builder, extensionPoints.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var extensionPoint in extensionPoints
                         .OrderBy(static item => item.Id, StringComparer.Ordinal))
            {
                Append(builder, extensionPoint.Id);
                Append(builder, ToDigestKind(extensionPoint.Kind));
                Append(builder, extensionPoint.Priority.ToString(CultureInfo.InvariantCulture));
                Append(builder, extensionPoint.Replaces ?? string.Empty);
                Append(builder, extensionPoint.DefaultFor ?? string.Empty);
                IEnumerable<string> conflicts = extensionPoint.Conflicts.IsDefault
                    ? Array.Empty<string>()
                    : extensionPoint.Conflicts.Order(StringComparer.Ordinal);
                foreach (var conflict in conflicts)
                    Append(builder, conflict);
            }
        }

        if (commands is { Count: > 0 })
        {
            Append(builder, "commands");
            Append(builder, commands.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var command in commands.OrderBy(static item => item.Id, StringComparer.Ordinal))
            {
                Append(builder, command.Id);
                Append(builder, command.Title ?? string.Empty);
                Append(builder, command.Description ?? string.Empty);
                Append(builder, command.Target);
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static string ToDigestKind(PluginExtensionPointKind kind) => kind switch
    {
        PluginExtensionPointKind.Provider => "provider",
        PluginExtensionPointKind.Registry => "registry",
        PluginExtensionPointKind.Pipeline => "pipeline",
        PluginExtensionPointKind.Hook => "hook",
        PluginExtensionPointKind.Override => "override",
        PluginExtensionPointKind.Ui => "ui",
        PluginExtensionPointKind.Event => "event",
        PluginExtensionPointKind.Command => "command",
        _ => kind.ToString()
    };

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length);
        builder.Append(':');
        builder.Append(value);
        builder.Append('\n');
    }
}
