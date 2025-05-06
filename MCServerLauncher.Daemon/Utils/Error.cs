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


    public string SimpleBackTrace()
    {
        var writer = new StringWriter();


        writer.Write("=> Error occurred: ");
        writer.WriteLine(Cause);

        if (CausedException is not null)
        {
            writer.Write("   Caused by     : ");
            writer.Write(string.Join("\n                  ", CausedException.ToString().Split("\n")));
        }

        if (InnerError is not null)
        {
            writer.WriteLine(InnerError.SimpleBackTrace());
        }

        return writer.ToString();
    }

    public override string ToString()
    {
        var writer = new StringWriter();
        writer.WriteLine(Cause);
        if (InnerError is not null)
        {
            writer.WriteLine("****************** Backtrace ******************");
            writer.Write(SimpleBackTrace());
        }

        return writer.ToString();
    }
}