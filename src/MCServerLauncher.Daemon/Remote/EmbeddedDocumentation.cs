using System.Reflection;

namespace MCServerLauncher.Daemon.Remote;

internal static class EmbeddedDocumentation
{
    private const string ResourcePrefix = "MCServerLauncher.Daemon.Resources.Docs.";
    private static readonly Assembly Assembly = typeof(EmbeddedDocumentation).Assembly;

    private static readonly IReadOnlyDictionary<string, DocumentResource> StaticResources =
        new Dictionary<string, DocumentResource>(StringComparer.OrdinalIgnoreCase)
        {
            ["/apifox.json"] = new("apifox.json", "application/json; charset=utf-8"),
        };

    public static bool TryGetResource(string requestPath, out DocumentResource document)
    {
        var path = NormalizePath(requestPath);
        if (StaticResources.TryGetValue(path, out document))
        {
            return true;
        }

        if (!path.StartsWith("/docs/protocol/", StringComparison.OrdinalIgnoreCase))
        {
            document = default;
            return false;
        }

        var relativePath = path["/docs/protocol/".Length..];
        if (relativePath.Length == 0 || relativePath.Contains("..", StringComparison.Ordinal))
        {
            document = default;
            return false;
        }

        var resourceName = "protocol." + relativePath.Replace('/', '.');
        document = new DocumentResource(resourceName, GetContentType(relativePath));
        return ResourceExists(document.ResourceName);
    }

    public static async Task<string> ReadContentAsync(DocumentResource document)
    {
        await using var stream = Assembly.GetManifestResourceStream(ResourcePrefix + document.ResourceName)
                                 ?? throw new FileNotFoundException(
                                     $"Embedded documentation resource not found: {document.ResourceName}");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static string NormalizePath(string requestPath)
    {
        var queryIndex = requestPath.IndexOf('?', StringComparison.Ordinal);
        return queryIndex >= 0 ? requestPath[..queryIndex] : requestPath;
    }

    private static bool ResourceExists(string resourceName)
    {
        return Assembly.GetManifestResourceInfo(ResourcePrefix + resourceName) is not null;
    }

    private static string GetContentType(string relativePath)
    {
        return Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".md" => "text/markdown; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            _ => "text/plain; charset=utf-8"
        };
    }

    internal readonly record struct DocumentResource(string ResourceName, string ContentType);
}
