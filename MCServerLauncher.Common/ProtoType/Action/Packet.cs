using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Serialization;
using SysTextJsonRequired = System.Text.Json.Serialization.JsonRequiredAttribute;
using SysTextJsonPropertyName = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace MCServerLauncher.Common.ProtoType.Action;

public record ActionRequest
{
    [SysTextJsonPropertyName("action")]
    [SysTextJsonRequired]
    public ActionType ActionType { get; init; }

    [SysTextJsonPropertyName("params")]
    [SysTextJsonRequired]
    public JsonElement? Parameter { get; init; }

    [SysTextJsonPropertyName("id")]
    [SysTextJsonRequired]
    public Guid Id { get; init; }
}

public record ActionResponse
{
    [SysTextJsonPropertyName("status")]
    [SysTextJsonRequired]
    public ActionRequestStatus RequestStatus { get; init; }

    [SysTextJsonPropertyName("retcode")]
    public int Retcode { get; init; }

    [SysTextJsonPropertyName("data")]
    [SysTextJsonRequired]
    public JsonElement? Data { get; init; }

    [SysTextJsonPropertyName("message")]
    [SysTextJsonRequired]
    public string Message { get; init; } = string.Empty;

    [SysTextJsonPropertyName("id")]
    [SysTextJsonRequired]
    public Guid Id { get; init; }
}
