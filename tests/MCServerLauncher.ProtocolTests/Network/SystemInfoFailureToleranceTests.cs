using MCServerLauncher.Daemon.Utils.Status;

namespace MCServerLauncher.ProtocolTests;

public class SystemInfoFailureToleranceTests
{
    [Fact]
    [Trait("Category", "Status")]
    [Trait("Category", "FailureTolerance")]
    public void ParseMacOsCpuUsage_EmptyTopOutput_ReturnsZero()
    {
        var usage = CpuInfoHelper.ParseMacOsCpuUsage("");

        Assert.Equal(0, usage);
    }

    [Fact]
    [Trait("Category", "Status")]
    [Trait("Category", "FailureTolerance")]
    public void ParseMacOsCpuUsage_PercentageOutput_ParsesValue()
    {
        var usage = CpuInfoHelper.ParseMacOsCpuUsage("12.34");

        Assert.Equal(12.34, usage);
    }
}
