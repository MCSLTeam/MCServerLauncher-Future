﻿namespace MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge.Json;

public class InstallV1 : Install
{
    public string ServerJarPath { get; set; } = "{ROOT}/minecraft_server.{MINECRAFT_VERSION}.jar";

    // public InstallV1(Install v0)
    // {
    //     Profile = v0.Profile;
    //     Version = v0.Version;
    //     Icon = v0.Icon;
    //     Minecraft = v0.Minecraft;
    //     Json = v0.Json;
    //     Logo = v0.Logo;
    //     Path = v0.Path;
    //     UrlIcon = v0.UrlIcon;
    //     Welcome = v0.Welcome;
    //     MirrorList = v0.MirrorList;
    //     HideClient = v0.HideClient;
    //     HideServer = v0.HideServer;
    //     HideExtract = v0.HideExtract;
    //     Libraries = v0.Libraries;
    //     Processors = v0.Processors;
    //     Data = v0.Data;
    // }
}