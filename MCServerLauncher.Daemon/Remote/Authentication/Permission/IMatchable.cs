namespace MCServerLauncher.Daemon.Remote.Authentication;

public interface IMatchable
{
    public bool Matches(Permission permission);
}