using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace MCServerLauncher.Daemon
{
    public class InstanceManager
    {
        async public Task CreateInstance(JObject instanceConfig)
        {
            string instanceType = instanceConfig["instanceType"]!.ToString();
            switch (instanceType)
            {
                case "MinecraftJavaServer":
                    await CreateMinecraftJavaInstance(instanceType, instanceConfig);
                    break;
                case "MinecraftForgeServer":
                    break;
                case "MinecraftBedrockServer":
                    break;
                case "OtherExecutable":
                    break;
                default:
                    break;
            }
        }
        async private Task CreateMinecraftJavaInstance(string instanceType, JObject instanceConfig)
        {
            #region InstanceConfig
            string OriginalServerCorePath = instanceConfig["instanceCoreFilePath"].ToString();
            //string JavaRuntimePath = instanceConfig["instanceJavaRuntimePath"].ToString();
            //int JvmMinimumMemory = instanceConfig["instanceJvmMinimumMemory"].ToObject<int>();
            //int JvmMaximumMemory = instanceConfig["instanceJvmMaximumMemory"].ToObject<int>();
            //List<string> JvmArguments = instanceConfig["instanceJvmArguments"].ToObject<List<string>>();
            string InstanceName = instanceConfig["instanceName"].ToString();
            #endregion

            #region InstanceCreation
            Directory.CreateDirectory(Path.Combine("Data", "InstanceData", InstanceName));
            File.Copy(OriginalServerCorePath,Path.Combine("Data", "InstanceData", InstanceName, Path.GetFileName(OriginalServerCorePath)));
            await WriteMinecraftJavaInstanceConfig(instanceConfig);
            #endregion
        }
        async private static Task WriteMinecraftJavaInstanceConfig(JObject instanceConfig)
        {
            MinecraftJavaInstanceConfig Config = new MinecraftJavaInstanceConfig
            {
                CoreName = Path.GetFileName(instanceConfig["instanceCoreFilePath"].ToString()),
                JavaRuntimePath = instanceConfig["instanceJavaRuntimePath"].ToString(),
                JvmMinimumMemory = instanceConfig["instanceJvmMinimumMemory"].ToObject<int>(),
                JvmMaximumMemory = instanceConfig["instanceJvmMaximumMemory"].ToObject<int>(),
                JvmArguments = instanceConfig["instanceJvmArguments"].ToObject<List<string>>(),
                InstanceName = instanceConfig["instanceName"].ToString()
            };
            await File.WriteAllTextAsync(
                Path.Combine("Data", "Configuration", "Instance", $"{instanceConfig["instanceName"]}.json"),
                JsonConvert.SerializeObject(Config, Formatting.Indented)
            );
        }
    }
}
