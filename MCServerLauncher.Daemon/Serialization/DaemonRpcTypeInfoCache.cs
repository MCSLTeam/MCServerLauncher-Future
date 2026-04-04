using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.ProtoType.Serialization;

namespace MCServerLauncher.Daemon.Serialization;

internal static class DaemonRpcTypeInfoCache<T>
{
    public static readonly JsonTypeInfo<T> TypeInfo = Resolve();

    private static JsonTypeInfo<T> Resolve()
    {
        var options = DaemonRpcJsonBoundary.StjOptions;
        var info = options.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>;
        return info ?? throw new NotSupportedException($"Daemon RPC boundary does not provide JsonTypeInfo for {typeof(T).FullName}");
    }
}
