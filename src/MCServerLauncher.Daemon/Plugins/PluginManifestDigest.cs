using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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
        IReadOnlyList<PluginManifestContractDependency>? contractDependencies = null)
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

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length);
        builder.Append(':');
        builder.Append(value);
        builder.Append('\n');
    }
}
