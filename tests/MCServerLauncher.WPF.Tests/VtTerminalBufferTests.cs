using MCServerLauncher.TerminalEmulator;

namespace MCServerLauncher.WPF.Tests;

public sealed class VtTerminalBufferTests
{
    [Fact]
    public void SplitUtf8AndCursorMovementRenderOneTerminalScreen()
    {
        var terminal = new TerminalBuffer(12, 3);
        var utf8 = "你"u8.ToArray();

        terminal.Feed(utf8.AsSpan(0, 1));
        terminal.Feed(utf8.AsSpan(1));
        terminal.Feed("abc\u001b[2DX"u8);

        Assert.StartsWith("你aXc", terminal.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CarriageReturnEraseLineAndResizePreserveVisibleContent()
    {
        var terminal = new TerminalBuffer(8, 3);

        terminal.Feed("progress\rOK\u001b[K\nnext"u8);
        terminal.Resize(10, 4);

        Assert.Equal($"OK{Environment.NewLine}next", terminal.Text);
        Assert.Equal(10, terminal.Columns);
        Assert.Equal(4, terminal.Rows);
    }
}
