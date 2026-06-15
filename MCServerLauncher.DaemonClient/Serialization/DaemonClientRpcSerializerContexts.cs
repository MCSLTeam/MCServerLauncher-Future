using System.Text.Json.Serialization;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Notification;
using MCServerLauncher.Common.ProtoType.Relay;
using MCServerLauncher.DaemonClient.Connection;

namespace MCServerLauncher.DaemonClient.Serialization;

/// <summary>
/// DaemonClient RPC-specific STJ context placeholders for ownership-local types.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(ActionResponse))]
[JsonSerializable(typeof(ActionRequest))]
[JsonSerializable(typeof(EventPacket))]
[JsonSerializable(typeof(NotificationPacket))]
[JsonSerializable(typeof(RelayPacket))]
[JsonSerializable(typeof(ClientConnectionConfig))]
internal partial class DaemonClientRpcSerializerContext : JsonSerializerContext
{
}
