using Newtonsoft.Json;

namespace MCServerLauncher.Common.ProtoType.Action;

[JsonConverter(typeof(ActionReturnCodeJsonConverter))]
public enum ActionReturnCode
{
    Ok = 0,
    ParseJson = 1401,
    PermissionDenied = 1403,
    ActionNotFound = 1404,
    ActionNotImplement = 1405,
    ParameterError = 1410,
    ParameterIsNull = 1411,
    ParameterParseError = 1412,
    InternalError = 1500,
    RequestLimitReached = 1501,
    IOException = 1511,
    FileNotFound = 1512,
    InstanceNotFound = 1521,
    InstanceAddFailed = 1522,
    InstanceNotRunning = 1523,
    InstanceIsRunning = 1524,
    InstanceProcessError = 1525,
}

internal class ActionReturnCodeJsonConverter : JsonConverter<ActionReturnCode>
{
    public override void WriteJson(JsonWriter writer, ActionReturnCode value, JsonSerializer serializer)
    {
        writer.WriteValue((int)value);
    }

    public override ActionReturnCode ReadJson(JsonReader reader, Type objectType, ActionReturnCode existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType != JsonToken.Integer)
            throw new JsonSerializationException("Can't parse action return code");

        var id = (reader.Value! as long?)!;
        if (!Enum.IsDefined(typeof(ActionReturnCode), (int)id))
            throw new JsonSerializationException($"Invalid action return code {id}");
        return (ActionReturnCode)id.Value;
    }
}