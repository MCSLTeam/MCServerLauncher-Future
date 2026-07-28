using MCServerLauncher.WPF.View.Components;

namespace MCServerLauncher.WPF.Tests;

public sealed class DaemonConnectionDefaultsTests
{
    [Fact]
    public void ConstructConnectDaemonDialog_DefaultsNewConnectionPortToDaemonPort()
    {
        var method = typeof(Utils).GetMethod(nameof(Utils.ConstructConnectDaemonDialog));

        Assert.NotNull(method);
        Assert.Equal("11452", method.GetParameters()[1].DefaultValue);
    }
}
