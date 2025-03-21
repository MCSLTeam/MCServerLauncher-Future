namespace MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;

public interface IMatchable
{
    public bool Matches(IMatchable permission);

    public static IMatchable Any(IEnumerable<IMatchable> matchables)
    {
        return MatchableImpl.Create(m => matchables.Any(matchable => matchable.Matches(m)));
    }

    public static IMatchable All(IEnumerable<IMatchable> matchables)
    {
        return MatchableImpl.Create(m => matchables.All(matchable => matchable.Matches(m)));
    }

    public static IMatchable Always()
    {
        return MatchableImpl.Create(_ => true);
    }


    public static IMatchable Never()
    {
        return MatchableImpl.Create(_ => false);
    }
}

public static class IMatchableExtension
{
    public static IMatchable Or(this IMatchable @this, IMatchable other)
    {
        return MatchableImpl.Create(m => @this.Matches(m) || other.Matches(m));
    }

    public static IMatchable And(this IMatchable @this, IMatchable other)
    {
        return MatchableImpl.Create(m => @this.Matches(m) && other.Matches(m));
    }
}

internal class MatchableImpl : IMatchable
{
    private readonly Func<IMatchable, bool> _matchFunc;

    private MatchableImpl(Func<IMatchable, bool> matchFunc)
    {
        _matchFunc = matchFunc;
    }

    public bool Matches(IMatchable matchable)
    {
        return _matchFunc.Invoke(matchable);
    }

    public static IMatchable Create(Func<IMatchable, bool> matchFunc)
    {
        return new MatchableImpl(matchFunc);
    }
}