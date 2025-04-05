using Newtonsoft.Json;

namespace MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge.Json;

public class Version
{
    public string Id { get; set; }

    [JsonProperty("downloads")] public Dictionary<string, Download> DownloadDictionary { get; set; }

    public Library[] Libraries { get; set; } = Array.Empty<Library>();

    public Download? GetDownload(string key)
    {
        return DownloadDictionary?.TryGetValue(key, out var download) == true ? download : null;
    }


    public class Download
    {
        public string? Sha1 { get; set; }

        public int Size { get; set; }

        public string? Url { get; set; }

        public bool Provided { get; set; } = false;

        public string GetUrl()
        {
            return string.IsNullOrEmpty(Url) || Provided ? string.Empty : Url;
        }
    }

    public class LibraryDownload : Download
    {
        public string Path { get; set; }
    }

    public class Library
    {
        public Artifact Name { get; set; }

        public Downloads? Downloads { get; set; }
    }

    public class Downloads
    {
        public LibraryDownload Artifact { get; set; }

        public Dictionary<string, LibraryDownload>? Classifiers { get; set; }

        public ISet<string> GetClassifiers()
        {
            return Classifiers?.Keys.ToHashSet() ?? new HashSet<string>();
        }
    }
}