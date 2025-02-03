namespace MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;

public class AndMatchables: IMatchable
{
    private readonly IMatchable[] _matchables;

    public AndMatchables(params IMatchable[] matchables)
    {
        _matchables = matchables;
    }


    public bool Matches(Permission permission)
    {
        return _matchables.All(matchable => matchable.Matches(permission));
    }
}