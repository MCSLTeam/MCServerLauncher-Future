namespace MCServerLauncher.Daemon.Utils;

public static class ErrorExtensions
{
    public static TError WithInner<TError>(this TError error, Error? inner)
        where TError : Error
    {
        error.InnerError = inner;
        return error;
    }

    public static TError CauseBy<TError>(this TError error, Exception exception)
        where TError : Error
    {
        error.CausedException = exception;
        return error;
    }
}