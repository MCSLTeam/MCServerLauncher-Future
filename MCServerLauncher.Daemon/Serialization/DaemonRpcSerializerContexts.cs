using System.Text.Json.Serialization;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote;

namespace MCServerLauncher.Daemon.Serialization;

/// <summary>
/// Daemon RPC-specific STJ context placeholders for ownership-local types.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(ActionError))]
[JsonSerializable(typeof(SubscribeEventParameter))]
[JsonSerializable(typeof(UnsubscribeEventParameter))]
[JsonSerializable(typeof(InstanceLogEventMeta))]
[JsonSerializable(typeof(Permission))]
[JsonSerializable(typeof(BinaryUploadResponse))]
[JsonSerializable(typeof(BinaryUploadErrorResponse))]
internal partial class DaemonRpcSerializerContext : JsonSerializerContext
{
}
