using MCServerLauncher.Daemon.FileManagement;
using Newtonsoft.Json;
using Serilog;

namespace MCServerLauncher.Daemon
{
    internal class Config
    {
        [JsonIgnore] private static Config _config;

        public readonly ushort Port;
        public readonly string Secret;
        public readonly string Token;

        public Config(ushort Port, string Secret, string Token)
        {
            this.Port = Port;
            this.Secret = Secret;
            this.Token = Token;
        }

        private static Config GetDefault()
        {
            return new Config(11451, GetRandomSecret(),Guid.NewGuid().ToString());
        }

        private static string GetRandomSecret()
        {
            return Guid.NewGuid().ToString();
        }


        public bool TrySave(string path = "config.json")
        {
            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
                return true;
            }
            catch (Exception e)
            {
                Log.Fatal($"Failed to save config file: {e.Message}");
                return false;
            }
        }

        private static Config LoadOrDefault(string path = "config.json")
        {
            return FileManager.ReadJsonAndBackupOr(path, GetDefault);
        }

        public static Config Get()
        {
            return _config ??= LoadOrDefault();
        }
    }
}