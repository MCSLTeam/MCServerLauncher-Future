using MCServerLauncher.Daemon.Remote.Authentication;

namespace MCServerLauncher.Daemon.Remote.Action;

public class RequirePermissions : System.Attribute, IMatchable
{
    private readonly IMatchable _matchable;

    public RequirePermissions(IMatchable matchable)
    {
        _matchable = matchable;
    }

    public bool Matches(Permission permission)
    {
        return _matchable.Matches(permission);
    }
}