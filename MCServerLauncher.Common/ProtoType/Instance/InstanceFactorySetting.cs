using Newtonsoft.Json;

namespace MCServerLauncher.Common.ProtoType.Instance;

/// <summary>
///     实例工厂设置
/// </summary>
public record InstanceFactorySetting : InstanceConfig
{
    [JsonProperty(Required = Required.Always)]
    public string Source { get; init; } = null!;

    [JsonProperty(Required = Required.Always)]
    public SourceType SourceType { get; init; }

    public InstanceFactoryMirror Mirror { get; init; } = InstanceFactoryMirror.None;
    public bool UsePostProcess { get; init; } = false;

    public InstanceConfig GetInstanceConfig()
    {
        return new InstanceConfig
        {
            McVersion = McVersion,
            Uuid = Uuid,
            InputEncoding = InputEncoding,
            OutputEncoding = OutputEncoding,
            InstanceType = InstanceType,
            Name = Name,
            JavaArgs = JavaArgs,
            JavaPath = JavaPath,
            Target = Target,
            TargetType = TargetType
        };
    }
}