namespace MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;

public class AlwaysMatchable : IMatchable
{
    private readonly bool _matches;

    public AlwaysMatchable(bool matches)
    {
        _matches = matches;
    }


    public bool Matches(Permission permission)
    {
        return _matches;
    }
}