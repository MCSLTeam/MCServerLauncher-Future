using System.Net;
using System.Text.Json.Serialization;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Plugins;
using MCServerLauncher.Daemon.Plugins.Configuration;
using Xunit;

namespace MCServerLauncher.ProtocolTests.Plugins;

public sealed partial class PluginHostServicesTests
{
    [Fact]
    public void HttpEndpointPolicyRejectsNonLoopbackAndConflicts()
    {
        var registry = new PluginHttpEndpointRegistry();
        Assert.True(registry.TryRegister(
            "mcsl.daemon",
            new IPEndPoint(IPAddress.Any, 11452),
            out _));

        var identity = new PluginIdentity("fixture.http", "1.0.0");
        var errors = new PluginErrorFactory(identity);
        var policy = new PluginHttpEndpointPolicy("fixture.http", registry, errors);

        var nonLoopback = policy.ValidateAndRegister("8.8.8.8", 18080);
        Assert.True(nonLoopback.IsErr(out var nonLoopbackError));
        Assert.Equal("plugin_http_bind_not_loopback", nonLoopbackError!.Code);

        var conflict = policy.ValidateAndRegister("127.0.0.1", 11452);
        Assert.True(conflict.IsErr(out var conflictError));
        Assert.Equal("plugin_http_port_conflict", conflictError!.Code);

        var ok = policy.ValidateAndRegister("127.0.0.1", 18080);
        Assert.True(ok.IsOk(out _));
        policy.Release("127.0.0.1", 18080);
    }

    [Fact]
    public async Task PrivateStorageEnforcesPathContainmentAndRoundTripsSnapshot()
    {
        var identity = new PluginIdentity("fixture.storage", "1.0.0");
        var errors = new PluginErrorFactory(identity);
        var config = DaemonPluginsConfig.Default;
        var storage = new PluginPrivateStorage(identity, config, errors);

        var bad = await storage.WriteSnapshotAsync(
            "../escape.json",
            new StorageDto("x"),
            StorageJsonContext.Default.StorageDto,
            CancellationToken.None);
        Assert.True(bad.IsErr(out var badError));
        Assert.Equal("plugin_storage_path_invalid", badError!.Code);

        var write = await storage.WriteSnapshotAsync(
            "state.json",
            new StorageDto("hello"),
            StorageJsonContext.Default.StorageDto,
            CancellationToken.None);
        Assert.True(write.IsOk(out _));

        var read = await storage.ReadSnapshotAsync(
            "state.json",
            StorageJsonContext.Default.StorageDto,
            CancellationToken.None);
        Assert.True(read.IsOk(out var dto));
        Assert.Equal("hello", dto!.Value);
    }

    [Fact]
    public async Task AuthenticationAcceptsMainTokenWhenAllowedAndRejectsOtherwise()
    {
        // Ensure AppConfig singleton is loaded (tests use default when missing).
        var config = MCServerLauncher.Daemon.AppConfig.Get();
        var identity = new PluginIdentity("fixture.auth", "1.0.0");
        var errors = new PluginErrorFactory(identity);
        var auth = new PluginAuthentication(errors);

        var denied = await auth.VerifyAsync(
            config.MainToken,
            "http://127.0.0.1:11453/mcp",
            new PluginAuthenticationOptions(AllowMainToken: false),
            CancellationToken.None);
        Assert.True(denied.IsErr(out _));

        var allowed = await auth.VerifyAsync(
            config.MainToken,
            "http://127.0.0.1:11453/mcp",
            new PluginAuthenticationOptions(AllowMainToken: true),
            CancellationToken.None);
        Assert.True(allowed.IsOk(out var principal));
        Assert.True(principal!.IsMainToken);
        Assert.Equal("daemon-main", principal.Subject);
        Assert.Contains("*", principal.Permissions);
    }

    public sealed record StorageDto(string Value);

    [JsonSerializable(typeof(StorageDto))]
    internal partial class StorageJsonContext : JsonSerializerContext;
}
