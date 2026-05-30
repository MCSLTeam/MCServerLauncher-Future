namespace MCServerLauncher.Common.ProtoType.Instance;

public static class InstanceTypeExtensions
{
    public static bool IsMinecraftJavaRuntimeType(this InstanceType type)
    {
        return type is
            InstanceType.MCJava or
            InstanceType.MCFabric or
            InstanceType.MCForge or
            InstanceType.MCNeoForge or
            InstanceType.MCQuilt or
            InstanceType.MCCleanroom or
            InstanceType.MCSpongeVanilla or
            InstanceType.MCSpongeForge or
            InstanceType.MCSpongeNeo or
            InstanceType.MCVanilla or
            InstanceType.MCCraftBukkit or
            InstanceType.MCSpigot or
            InstanceType.MCPaper or
            InstanceType.MCLeaf or
            InstanceType.MCLeaves or
            InstanceType.MCFolia or
            InstanceType.MCCanvas or
            InstanceType.MCPufferfish or
            InstanceType.MCPurpur or
            InstanceType.MCMohist or
            InstanceType.MCBanner or
            InstanceType.MCYouer or
            InstanceType.MCThermos or
            InstanceType.MCCrucible or
            InstanceType.MCTaiyitist or
            InstanceType.MCCatServer or
            InstanceType.MCArclight;
    }

    public static bool SupportsMinecraftBoardWidgets(this InstanceType type)
    {
        return type.IsMinecraftJavaRuntimeType();
    }

    public static bool RequiresNumericMinecraftVersion(this InstanceType type)
    {
        return type is
            InstanceType.MCFabric or
            InstanceType.MCForge or
            InstanceType.MCNeoForge or
            InstanceType.MCQuilt or
            InstanceType.MCCleanroom or
            InstanceType.MCVanilla or
            InstanceType.MCCraftBukkit or
            InstanceType.MCSpigot or
            InstanceType.MCPaper or
            InstanceType.MCLeaf or
            InstanceType.MCLeaves or
            InstanceType.MCFolia or
            InstanceType.MCCanvas or
            InstanceType.MCPufferfish or
            InstanceType.MCPurpur or
            InstanceType.MCMohist or
            InstanceType.MCBanner or
            InstanceType.MCYouer or
            InstanceType.MCThermos or
            InstanceType.MCCrucible or
            InstanceType.MCTaiyitist or
            InstanceType.MCCatServer or
            InstanceType.MCArclight;
    }

    public static bool IsMinecraftLikeType(this InstanceType type)
    {
        return type is not InstanceType.Universal and not InstanceType.SteamServer and not InstanceType.Terraria and
               not InstanceType.TShock and not InstanceType.TDSM;
    }

    public static bool IsGenericFallbackType(this InstanceType type)
    {
        return type is InstanceType.Universal or InstanceType.MCJava or InstanceType.MCBedrock or InstanceType.Terraria;
    }
}
