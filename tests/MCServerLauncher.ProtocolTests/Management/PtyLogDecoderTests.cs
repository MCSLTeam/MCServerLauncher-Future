using System.Text;
using MCServerLauncher.Daemon.Management.Communicate;

namespace MCServerLauncher.ProtocolTests;

public sealed class PtyLogDecoderTests
{
    [Fact]
    public void SplitUtf8AndAnsiSequencesProduceCompleteSanitizedLines()
    {
        var decoder = new PtyLogDecoder(Encoding.UTF8);
        var bytes = Encoding.UTF8.GetBytes("\u001b[38;2;1;2;3m你\u001b[0m\nnext");

        var first = decoder.Append(bytes.AsSpan(0, 4));
        var second = decoder.Append(bytes.AsSpan(4, 8));
        var third = decoder.Append(bytes.AsSpan(12));
        var final = decoder.Complete();

        Assert.Empty(first);
        Assert.Empty(second);
        Assert.Equal(["你"], third);
        Assert.Equal(["next"], final);
    }

    [Fact]
    public void IncompleteAnsiSequenceDoesNotLeakIntoHistory()
    {
        var decoder = new PtyLogDecoder(Encoding.UTF8);

        decoder.Append("ready\u001b[31"u8);
        var lines = decoder.Complete();

        Assert.Equal(["ready"], lines);
    }
}
