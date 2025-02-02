namespace MCServerLauncher.Daemon.Remote.Authentication;

public class OrMatchables : IMatchable
{
    private readonly IMatchable[] _matchables;

    public OrMatchables(params IMatchable[] matchables)
    {
        _matchables = matchables;
    }


    public bool Matches(Permission permission)
    {
        return _matchables.Any(matchable => matchable.Matches(permission));
    }
}