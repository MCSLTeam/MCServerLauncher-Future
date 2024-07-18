using Newtonsoft.Json;

namespace MCServerLauncher.Daemon
{
    internal class Config
    {
        public readonly UInt16 Port;
        public readonly string PersistentToken;
        private Dictionary<string, DateTime> TemporaryTokens;

        public Config(UInt16 Port, string PersistentToken, Dictionary<string, DateTime> TemporaryTokens)
        {
            this.Port = Port;
            this.PersistentToken = PersistentToken;
            this.TemporaryTokens = TemporaryTokens;
        }

        public static Config Default()
        {
            return new Config(11451, GetRandomToken(), new Dictionary<string, DateTime>());
        }

        private static string GetRandomToken()
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
            catch (Exception)
            {
                return false;
            }
        }

        private static Config Load(string path = "config.json")
        {
            return JsonConvert.DeserializeObject<Config>(File.ReadAllText(path));
        }

        public bool TryCreateTemporaryToken(long seconds, out string token, out DateTime expired)
        {
            return TryUpdateTemporaryTokens(token = GetRandomToken(), seconds, out expired);
        }

        public bool TryUpdateTemporaryTokens(string Token, long seconds, out DateTime expired)
        {
            expired = DateTime.Now.AddSeconds(seconds);
            TemporaryTokens[Token] = expired;
            return TrySave();
        }

        public bool ValidateTemporaryToken(string token)
        {
            if (TemporaryTokens.ContainsKey(token))
            {
                if (TemporaryTokens[token] > DateTime.Now) return true;

                TemporaryTokens.Remove(token);
                TrySave();
            }

            return false;
        }

        public static bool TryLoadOrDefault(string path, out Config config)
        {
            try
            {
                config = Load(path);
                config.TemporaryTokens = config.TemporaryTokens ?? new Dictionary<string, DateTime>();
                return true;
            }
            catch (Exception)
            {
                config = Default();
                config.TrySave(path);
                return false;
            }
        }
    }
}