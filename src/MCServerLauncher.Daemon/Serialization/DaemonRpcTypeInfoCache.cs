using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.ProtoType.Serialization;

namespace MCServerLauncher.Daemon.Serialization;

/// <summary>
/// Typed cache for daemon RPC boundary <see cref="JsonTypeInfo{T}"/> lookups.
/// Resolves exclusively from Common + Daemon source-generated contexts (no reflection fallback).
/// Types not registered in source-gen contexts throw <see cref="NotSupportedException"/>
/// rather than silently falling back to ambient reflection resolution.
/// </summary>
internal static class DaemonRpcTypeInfoCache<T>
{
    private static readonly JsonTypeInfo<T>? _cachedTypeInfo = TryResolveSourceGen();

    /// <summary>
    /// Gets the cached <see cref="JsonTypeInfo{T}"/> for type <typeparamref name="T"/>.
    /// Resolved from source-gen contexts only; throws if <typeparamref name="T"/> is not registered.
    /// </summary>
    public static JsonTypeInfo<T> TypeInfo =>
        _cachedTypeInfo ?? throw new NotSupportedException(
            $"Daemon RPC type-info cache does not provide source-generated JsonTypeInfo for {typeof(T).FullName}. "
            + $"Type must be registered in Common or Daemon serializer contexts.");

    private static JsonTypeInfo<T>? TryResolveSourceGen()
    {
        try
        {
            // Source-gen-only resolution: use options with reflection fallback disabled
            // so only explicitly registered types are resolvable through the cache.
            var options = DaemonRpcJsonBoundary.SourceGenStjOptions;
            return options.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>;
        }
        catch (NotSupportedException)
        {
            // STJ throws when the type is not in any source-gen context.
            // Return null so the property getter can throw our clear, catchable exception.
            return null;
        }
    }
}
