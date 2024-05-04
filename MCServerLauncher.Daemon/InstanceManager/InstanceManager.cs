using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace MCServerLauncher.Daemon
{
    public class InstanceManager
    {
        public void CreateInstance(JObject instanceConfig)
        {
            string instanceType = instanceConfig["instanceType"]!.ToString();
            switch (instanceType)
            {
                case "MinecraftJavaServer":
                    CreateMinecraftJavaInstance(instanceType, instanceConfig);
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
        private void CreateMinecraftJavaInstance(string instanceType, JObject instanceConfig)
        {
            #region InstanceConfig
            string OriginalServerCorePath = instanceConfig["instanceCoreFilePath"].ToString();
            string JavaRuntimePath = instanceConfig["instanceJavaRuntimePath"].ToString();
            int JvmMinimumMemory = instanceConfig["instanceJvmMinimumMemory"].ToObject<int>();
            int JvmMaximumMemory = instanceConfig["instanceJvmMaximumMemory"].ToObject<int>();
            List<string> JvmArguments = instanceConfig["instanceJvmArguments"].ToObject<List<string>>();
            string InstanceName = instanceConfig["instanceName"].ToString();
            #endregion

            #region InstanceCreation
            Directory.CreateDirectory(Path.Combine("Data", "InstanceData", InstanceName));
            File.Copy(OriginalServerCorePath,Path.Combine("Data", "InstanceData", InstanceName, Path.GetFileName(OriginalServerCorePath)));
            WriteMinecraftJavaInstanceConfig(instanceConfig);
            #endregion
        }
        private void WriteMinecraftJavaInstanceConfig(JObject instanceConfig)
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
            File.WriteAllText(
                Path.Combine("Data", "Configuration", "Instance", instanceConfig["instanceName"].ToString() + ".json"),
                JsonConvert.SerializeObject(Config, Formatting.Indented)
            );
        }
    }
}
