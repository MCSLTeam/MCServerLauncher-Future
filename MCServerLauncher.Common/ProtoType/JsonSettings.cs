using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Serialization;

namespace MCServerLauncher.Common.ProtoType;

public static class JsonSettings
{
    public static readonly JsonSerializerOptions Settings = StjResolver.CreateDefaultOptions();
}
