namespace MCServerLauncher.Common.ProtoType.Instance;

[Flags]
public enum InstanceCategory
{
    Generic = 0,
    MinecraftJava = 1,
    MinecraftBedrock = 2,
    Terraria = 4,
    Steam = 8,
    Proxy = 16,
    Utility = 32
}
