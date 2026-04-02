using Newtonsoft.Json;
using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Serialization;
using SysTextJsonRequired = System.Text.Json.Serialization.JsonRequiredAttribute;
using SysTextJsonPropertyName = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace MCServerLauncher.Common.ProtoType.Action;

public record ActionRequest
{
    [JsonProperty(PropertyName = "action", Required = Required.Always)]
    [SysTextJsonPropertyName("action")]
    [SysTextJsonRequired]
    public ActionType ActionType { get; init; }

    [JsonProperty(PropertyName = "params", Required = Required.AllowNull)]
    [SysTextJsonPropertyName("params")]
    [SysTextJsonRequired]
    [JsonConverter(typeof(NewtonsoftJsonElementConverter))]
    public JsonElement? Parameter { get; init; }

    [JsonProperty(PropertyName = "id", Required = Required.Always)]
    [SysTextJsonPropertyName("id")]
    [SysTextJsonRequired]
    public Guid Id { get; init; }
}

public record ActionResponse
{
    [JsonProperty(PropertyName = "status", Required = Required.Always)]
    [SysTextJsonPropertyName("status")]
    [SysTextJsonRequired]
    public ActionRequestStatus RequestStatus { get; init; }

    [JsonProperty(PropertyName = "retcode")]
    [SysTextJsonPropertyName("retcode")]
    public int Retcode { get; init; }

    [JsonProperty(PropertyName = "data", Required = Required.AllowNull)]
    [SysTextJsonPropertyName("data")]
    [SysTextJsonRequired]
    [JsonConverter(typeof(NewtonsoftJsonElementConverter))]
    public JsonElement? Data { get; init; }

    [JsonProperty(PropertyName = "message", Required = Required.Always)]
    [SysTextJsonPropertyName("message")]
    [SysTextJsonRequired]
    public string Message { get; init; } = string.Empty;

    [JsonProperty(Required = Required.Always)]
    [SysTextJsonPropertyName("id")]
    [SysTextJsonRequired]
    public Guid Id { get; init; }
}
