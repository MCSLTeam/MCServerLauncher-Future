namespace MCServerLauncher.Common.ProtoType.Instance;

/// <summary>
/// Instance process console I/O mode.
/// <see cref="Pipe"/> is the default line-oriented redirected stdin/stdout model.
/// <see cref="Pty"/> attaches a real pseudo-terminal (Unix PTY / Windows ConPTY).
/// </summary>
public enum ConsoleMode
{
    Pipe = 0,
    Pty = 1
}
