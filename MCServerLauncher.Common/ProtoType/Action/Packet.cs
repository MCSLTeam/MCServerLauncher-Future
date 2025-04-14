using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.Common.ProtoType.Action;

public record ActionRequest
{
    [JsonProperty(PropertyName = "action", Required = Required.Always)]
    public ActionType ActionType { get; init; }

    [JsonProperty(PropertyName = "params", Required = Required.AllowNull)]
    public JToken? Parameter { get; init; }

    [JsonProperty(PropertyName = "id", Required = Required.Always)]
    public Guid Id { get; init; }
}

public record ActionResponse
{
    [JsonProperty(PropertyName = "status", Required = Required.Always)]
    public ActionRequestStatus RequestStatus { get; init; }

    [JsonProperty(PropertyName = "retcode")]
    public int Retcode { get; init; }

    [JsonProperty(Required = Required.Default)]
    public JToken? Data { get; init; }

    public string Message { get; init; } = string.Empty;

    [JsonProperty(Required = Required.Always)]
    public Guid Id { get; init; }
}