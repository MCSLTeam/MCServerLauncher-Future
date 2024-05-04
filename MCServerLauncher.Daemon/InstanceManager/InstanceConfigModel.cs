using Newtonsoft.Json;


public class MinecraftJavaInstanceConfig
{
    public string CoreName { get; set; }
    public string JavaRuntimePath { get; set; }
    public int JvmMinimumMemory { get; set; }
    public int JvmMaximumMemory { get; set; }
    public List<string> JvmArguments { get; set; }
    public string InstanceName { get; set; }

    public string GetSerializedJsonString()
    {
        return JsonConvert.SerializeObject(this, Formatting.Indented);
    }
}