namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

public static class Registration
{
    public static ActionHandlerRegistry RegisterHandlers(this ActionHandlerRegistry registry)
    {
        HandleEvent.Register(registry);
        HandleFileDownload.Register(registry);
        HandleFileInfo.Register(registry);
        HandleFileUpload.Register(registry);
        HandleInstance.Register(registry);
        HandleMisc.Register(registry);
        return registry;
    }
}