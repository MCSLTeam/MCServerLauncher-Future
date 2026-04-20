using SysTextJsonRequired = System.Text.Json.Serialization.JsonRequiredAttribute;

namespace MCServerLauncher.Common.ProtoType.Instance;

/// <summary>
///     实例工厂设置
/// </summary>
public record InstanceFactorySetting : InstanceConfig
{
    [SysTextJsonRequired]
    public string Source { get; init; } = null!;

    [SysTextJsonRequired]
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
            Arguments = Arguments,
            JavaPath = JavaPath,
            Target = Target,
            TargetType = TargetType
        };
    }
}