using MCServerLauncher.TerminalEmulator;

namespace MCServerLauncher.WPF.Tests;

public sealed class TerminalEmulatorTests
{
    [Fact]
    public void FeedAppliesAnsiTrueColorToCells()
    {
        var terminal = new TerminalBuffer(8, 2);

        terminal.Feed("\u001b[38;2;18;52;86mA"u8);

        var cell = terminal.GetCell(0, 0);
        Assert.Equal('A', cell.Character);
        Assert.Equal(TerminalColor.FromRgb(18, 52, 86), cell.Foreground);
    }

    [Fact]
    public void BackspaceMovesCursorAndOverwritesPreviousCell()
    {
        var terminal = new TerminalBuffer(8, 2);

        terminal.Feed("ab\bc"u8);

        Assert.Equal('a', terminal.GetCell(0, 0).Character);
        Assert.Equal('c', terminal.GetCell(0, 1).Character);
    }

    [Fact]
    public void ViewTextDoesNotPadCurrentLineToCursorColumn()
    {
        var terminal = new TerminalBuffer(8, 2);

        terminal.Feed("ab"u8);

        Assert.Equal("ab", terminal.ViewText);
        Assert.Equal("ab", terminal.Text);
        Assert.Equal(2, terminal.CursorColumn);
    }

    [Fact]
    public void ClearResetsScrollbackCursorAndDecoderState()
    {
        var terminal = new TerminalBuffer(4, 2);

        terminal.Feed("abcd\nefgh\nij"u8);
        terminal.Clear();

        Assert.Equal(0, terminal.ScrollbackLineCount);
        Assert.Equal(0, terminal.CursorRow);
        Assert.Equal(0, terminal.CursorColumn);
        Assert.Equal(string.Empty, terminal.ViewText);
    }

    [Fact]
    public void OscSequenceTerminatedByEscapeContinuesWithFollowingCsiOutput()
    {
        var terminal = new TerminalBuffer(16, 2);

        terminal.Feed("\u001b]0;title\u001b[31mA"u8);

        Assert.Equal('A', terminal.GetCell(0, 0).Character);
        Assert.Equal(TerminalColor.FromRgb(0xE0, 0x6C, 0x75), terminal.GetCell(0, 0).Foreground);
    }

    [Fact]
    public void OscSequenceTerminatedByStringTerminatorDoesNotRenderTerminator()
    {
        var terminal = new TerminalBuffer(16, 2);

        terminal.Feed("\u001b]0;title\u001b\\A"u8);

        Assert.Equal('A', terminal.GetCell(0, 0).Character);
    }

    [Fact]
    public void ClearTerminalLeavesNoPaddingOnEmptyScreen()
    {
        var terminal = new TerminalBuffer(12, 3);

        terminal.Feed("hello"u8);
        terminal.Clear();

        Assert.Equal(string.Empty, terminal.ViewText);
        Assert.Equal(string.Empty, terminal.Text);
    }

    [Fact]
    public void EraseDisplayDoesNotMoveCursor()
    {
        var terminal = new TerminalBuffer(12, 3);

        terminal.Feed("hello\u001b[2J"u8);

        Assert.Equal(0, terminal.CursorRow);
        Assert.Equal(5, terminal.CursorColumn);
        Assert.Equal(string.Empty, terminal.Text);
    }
}