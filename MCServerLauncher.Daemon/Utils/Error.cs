namespace MCServerLauncher.Daemon.Utils;

public class Error
{
    protected readonly string? ProtectedCause;

    public Error(string? protectedCause = null)
    {
        ProtectedCause = protectedCause;
    }

    public virtual string Cause => ProtectedCause ?? CausedException?.Message ?? "Error occurred";

    public Exception? CausedException { get; set; }
    public Error? InnerError { get; set; }

    public static Error FromException(Exception exception)
    {
        return new Error().CauseBy(exception);
    }

    public static Error FromString(string cause)
    {
        return new Error(cause);
    }

    public static implicit operator Error(string cause)
    {
        return FromString(cause);
    }

    public static implicit operator Error(Exception exception)
    {
        return FromException(exception);
    }


    public override string ToString()
    {
        var writer = new StringWriter();
        if (InnerError is not null) writer.WriteLine(InnerError.ToString());

        writer.Write("=> Error occurred: ");
        writer.Write(Cause);
        if (CausedException is not null)
        {
            writer.WriteLine("; Caused by:");
            writer.Write(CausedException.ToString());
        }

        return writer.ToString();
    }
}