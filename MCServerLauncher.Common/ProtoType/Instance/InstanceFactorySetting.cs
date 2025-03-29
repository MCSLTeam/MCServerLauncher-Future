namespace MCServerLauncher.Common.ProtoType.Instance;

public record InstanceFactorySetting : InstanceConfig
{
    public string McVersion { get; set; } = null!;
    public string Source { get; set; } = null!;
    public SourceType SourceType { get; set; }
    public bool UsePostProcess { get; set; } = false;

    public InstanceConfig GetInstanceConfig()
    {
        return new InstanceConfig
        {
            Uuid = Uuid,
            InputEncoding = InputEncoding,
            OutputEncoding = OutputEncoding,
            InstanceType = InstanceType,
            Name = Name,
            WorkingDirectory = WorkingDirectory,
            JavaArgs = JavaArgs,
            JavaPath = JavaPath,
            Target = Target,
            TargetType = TargetType,
        };
    }
}