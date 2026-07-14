using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Serialization;

namespace MCServerLauncher.Daemon.Serialization;

/// <summary>
/// Daemon-owned serializer boundary for daemon-managed persistence JSON.
/// </summary>
public static class DaemonPersistenceJsonBoundary
{
    public static readonly JsonSerializerOptions StjOptions = CreateStjOptions();
    public static readonly JsonSerializerOptions StjWriteIndentedOptions = CreateStjOptions(writeIndented: true);

    public static IJsonTypeInfoResolver CreateStjResolver()
    {
        return DaemonPersistenceSerializerContext.Default;
    }

    public static JsonSerializerOptions CreateStjOptions(
        bool writeIndented = false)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            TypeInfoResolver = CreateStjResolver(),
            IncludeFields = true,
            WriteIndented = writeIndented
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        options.Converters.Add(new GuidStjConverter());
        options.Converters.Add(new EncodingStjConverter());
        options.Converters.Add(new PlaceHolderStringStjConverter());
        return options;
    }
}
