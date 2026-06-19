namespace MCServerLauncher.Common.ProtoType.Instance;

using static InstanceCategory;
using M = InstanceTypeMetadataAttribute;

public enum InstanceType
{
    [M(Category = Generic, IsGenericFallback = true)]
    Universal,

    [M(Category = Steam)]
    SteamServer,

    #region Minecraft Java Servers

    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, IsGenericFallback = true)]
    MCJava,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCFabric,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCForge,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCNeoForge,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCQuilt,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCCleanroom,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true)]
    MCSpongeVanilla,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true)]
    MCSpongeForge,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true)]
    MCSpongeNeo,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCVanilla,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCCraftBukkit,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCSpigot,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCPaper,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCLeaf,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCLeaves,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCFolia,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCCanvas,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCPufferfish,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCPurpur,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCMohist,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCBanner,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCYouer,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCThermos,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCCrucible,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCTaiyitist,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCCatServer,
    [M(Category = MinecraftJava, SupportsMinecraftBoardWidgets = true, RequiresNumericVersion = true)]
    MCArclight,
    [M(Category = MinecraftJava | Proxy)]
    MCBungeeCord,
    [M(Category = MinecraftJava | Proxy)]
    MCVelocity,
    [M(Category = MinecraftJava | Proxy)]
    MCWaterfall,
    [M(Category = MinecraftJava | Proxy)]
    MCTravertine,
    [M(Category = MinecraftJava | Utility)]
    MCViaVersion,
    [M(Category = MinecraftJava | Utility)]
    MCGeyser,
    [M(Category = MinecraftJava | Utility)]
    MCDReforged,

    #endregion

    #region Minecraft Bedrock Servers

    [M(Category = MinecraftBedrock, IsGenericFallback = true)]
    MCBedrock,
    [M(Category = MinecraftBedrock)]
    MCNukkit,
    [M(Category = MinecraftBedrock)]
    MCBDS,
    [M(Category = MinecraftBedrock)]
    MCCloudburst,
    [M(Category = MinecraftBedrock)]
    MCPocketMine,

    #endregion

    #region Terraria

    [M(Category = InstanceCategory.Terraria, IsGenericFallback = true)]
    Terraria,
    [M(Category = InstanceCategory.Terraria)]
    TShock,
    [M(Category = InstanceCategory.Terraria)]
    TDSM,

    #endregion
}
