namespace MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;

public interface IMatchable
{
    public bool Matches(Permission permission);
}