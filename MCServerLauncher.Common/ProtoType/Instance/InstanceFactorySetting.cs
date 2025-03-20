namespace MCServerLauncher.Common.ProtoType.Instance;

public class InstanceFactorySetting : InstanceConfig
{
    public string McVersion { get; set; }
    public string Source { get; set; }
    public SourceType SourceType { get; set; }
    public bool UsePostProcess { get; set; } = false;

    public InstanceConfig GetInstanceConfig()
    {
        return this;
    }
}