using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;

namespace MCServerLauncher.WPF.Tests.Services;

public sealed class DaemonErrorLocalizationTests
{
    [Theory]
    [InlineData("connection.closed")]
    [InlineData("client.not_ready")]
    public void ConnectionAndClientErrorsUseConnectionMessage(string code)
    {
        var error = new TransportDaemonError(code, "failure");

        Assert.Equal(Lang.Tr["DaemonConnectionError"], DaemonErrorLocalization.GetMessage(error));
    }

    [Theory]
    [InlineData("instance.not_found")]
    [InlineData("validation.failed")]
    public void OtherErrorsUseGenericStatusMessage(string code)
    {
        var error = new ValidationDaemonError(code, "failure");

        Assert.Equal(Lang.Tr["Status_Error"], DaemonErrorLocalization.GetMessage(error));
    }
}
