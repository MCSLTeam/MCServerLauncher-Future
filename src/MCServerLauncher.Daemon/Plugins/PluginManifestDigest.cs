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
        IReadOnlyList<string> features)
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
