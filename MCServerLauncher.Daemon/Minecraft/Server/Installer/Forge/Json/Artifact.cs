using Newtonsoft.Json;

namespace MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge.Json;

public class Artifact
{
    private string? _descriptor;
    private string? _filename;

    // Cached values
    private string? _path;

    // Descriptor parts: group:name:version[:classifier][@extension]
    public string Domain { get; private set; }
    public string Name { get; private set; }
    public string Version { get; private set; }
    public string? Classifier { get; private set; }
    public string Extension { get; private set; } = "jar";

    // Properties
    public string Descriptor => _descriptor ??= $"{Domain}:{Name}:{Version}" +
                                                (Classifier != null ? $":{Classifier}" : "") +
                                                (Extension != "jar" ? $"@{Extension}" : "");

    public string Path => _path!;
    public string Filename => _filename!;

    public static Artifact FromDescriptor(string descriptor)
    {
        var artifact = new Artifact
        {
            _descriptor = descriptor
        };

        var parts = descriptor.Split(':');
        if (parts.Length < 3)
            throw new ArgumentException("Invalid artifact descriptor");

        artifact.Domain = parts[0];
        artifact.Name = parts[1];

        // Handle extension (@ suffix)
        var lastPart = parts[^1];
        var extIndex = lastPart.IndexOf('@');
        if (extIndex != -1)
        {
            artifact.Extension = lastPart[(extIndex + 1)..];
            parts[^1] = lastPart[..extIndex];
        }

        artifact.Version = parts[2];

        if (parts.Length > 3) artifact.Classifier = parts[3];

        // Build filename
        artifact._filename = $"{artifact.Name}-{artifact.Version}";
        if (!string.IsNullOrEmpty(artifact.Classifier))
            artifact._filename += $"-{artifact.Classifier}";
        artifact._filename += $".{artifact.Extension}";

        // Build path
        artifact._path = System.IO.Path.Combine(
            artifact.Domain.Replace('.', System.IO.Path.DirectorySeparatorChar),
            artifact.Name,
            artifact.Version,
            artifact._filename
        );

        return artifact;
    }

    public string GetLocalPath(string basePath)
    {
        return System.IO.Path.Combine(basePath, _path!);
    }

    public override string ToString()
    {
        return Descriptor;
    }

    public class ArtifactConverter : JsonConverter<Artifact>
    {
        public override void WriteJson(JsonWriter writer, Artifact? value, JsonSerializer serializer)
        {
            if (value == null)
                writer.WriteNull();
            else
                writer.WriteValue(value.Descriptor);
        }

        public override Artifact ReadJson(
            JsonReader reader,
            Type objectType,
            Artifact? existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            return reader.TokenType == JsonToken.String
                ? FromDescriptor(reader.Value!.ToString()!)
                : null!;
        }
    }
}