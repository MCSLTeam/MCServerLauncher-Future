namespace MCServerLauncher.Common.ProtoType.Instance;

/// <summary>
///     实例类型
/// </summary>
public enum InstanceType
{
    Universal, //  (默认值)

    SteamServer,

    #region Minecraft Java Servers
    MCJava, // TEMPLATE
    MCFabric, // TEMPLATE
    //MCLegecyFabric,
    MCForge, // TEMPLATE
    MCNeoForge, // TEMPLATE
    MCQuilt, // TEMPLATE
    MCCleanroom, // TEMPLATE
    MCSpongeVanilla, // TEMPLATE
    MCSpongeForge, // TEMPLATE
    MCSpongeNeo, // TEMPLATE
    MCVanilla,
    MCCraftBukkit,
    MCSpigot,
    MCPaper,
    MCLeaf,
    MCLeaves,
    MCFolia,
    MCCanvas, // Fork of Folia
    MCPufferfish,
    MCPurpur,
    MCMohist,
    MCBanner,
    MCYouer,
    MCThermos,
    MCCrucible,
    MCTaiyitist,
    MCCatServer,
    MCArclight,
    MCBungeeCord, // TEMPLATE
    MCVelocity, // TEMPLATE
    MCWaterfall, // TEMPLATE
    MCTravertine, // TEMPLATE
    MCViaVersion, // TEMPLATE
    MCGeyser, // TEMPLATE
    MCDReforged, // TEMPLATE
    #endregion

    #region Minecraft Bedrock Servers
    MCBedrock,
    MCNukkit,
    MCBDS,
    MCCloudburst,
    MCPocketMine,
    #endregion

    #region Terraria
    Terraria,
    TShock,
    TDSM,
    #endregion
}