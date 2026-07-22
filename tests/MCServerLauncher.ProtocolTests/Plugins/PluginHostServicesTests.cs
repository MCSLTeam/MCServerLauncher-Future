using System.Text.Json.Serialization;
using System.Text.Json;
using MCServerLauncher.Daemon;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Plugins;
using MCServerLauncher.Daemon.Plugins.Configuration;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Remote.Authentication;

namespace MCServerLauncher.ProtocolTests.Plugins;

public sealed class PluginHostServicesTests
{
    [Fact]
    public void Configuration_MissingFile_IsDistinguishableFromInvalidJson()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plugin-config-").FullName;
        try
        {
            var identity = new PluginIdentity("fixture.config", "1.0.0");
            var errors = new PluginErrorFactory(identity);
            var missing = new PluginConfiguration(root, errors);
            Assert.False(missing.Exists);
            var missingResult = missing.Get(PluginHostServicesJsonContext.Default.SampleConfig);
            Assert.True(missingResult.IsErr(out var missingError));
            Assert.Equal("plugin_config_missing", missingError!.Code);

            File.WriteAllText(Path.Combine(root, "config.json"), "{ not-json");
            Assert.Throws<PluginManifestException>(() => new PluginConfiguration(root, errors));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Configuration_ValidJson_BindsWithExplicitMetadata()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plugin-config-ok-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "config.json"), """{"name":"alpha","count":3}""");
            var identity = new PluginIdentity("fixture.config-ok", "1.0.0");
            var config = new PluginConfiguration(root, new PluginErrorFactory(identity));
            Assert.True(config.Exists);
            var result = config.Get(PluginHostServicesJsonContext.Default.SampleConfig);
            Assert.True(result.IsOk(out var value));
            Assert.Equal("alpha", value!.Name);
            Assert.Equal(3, value.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PrivateStorage_EnforcesRelativeContainmentAndQuota()
    {
        var identity = new PluginIdentity($"fixture.storage.{Guid.NewGuid():N}", "1.0.0");
        var errors = new PluginErrorFactory(identity);
        var pluginsConfig = new DaemonPluginsConfig
        {
            Storage = new PluginStorageConfig
            {
                DefaultQuotaBytes = 64,
                DefaultMaxFiles = 2,
            },
        };
        var pluginRoot = Path.Combine(FileManager.Root, "plugins", identity.Id);
        try
        {
            var storage = new PluginPrivateStorage(identity, pluginsConfig, errors);

            var write = await storage.WriteSnapshotAsync(
                "state.json",
                new SampleConfig("beta", 1),
                PluginHostServicesJsonContext.Default.SampleConfig,
                CancellationToken.None);
            Assert.True(write.IsOk(out _));

            var read = await storage.ReadSnapshotAsync(
                "state.json",
                PluginHostServicesJsonContext.Default.SampleConfig,
                CancellationToken.None);
            Assert.True(read.IsOk(out var value));
            Assert.Equal("beta", value!.Name);

            var traversal = await storage.WriteSnapshotAsync(
                "../escape.json",
                new SampleConfig("nope", 0),
                PluginHostServicesJsonContext.Default.SampleConfig,
                CancellationToken.None);
            Assert.True(traversal.IsErr(out var pathError));
            Assert.Equal("plugin_storage_path_invalid", pathError!.Code);

            var big = await storage.WriteSnapshotAsync(
                "big.json",
                new SampleConfig(new string('x', 200), 9),
                PluginHostServicesJsonContext.Default.SampleConfig,
                CancellationToken.None);
            Assert.True(big.IsErr(out var quotaError));
            Assert.Equal("plugin_storage_byte_quota", quotaError!.Code);
        }
        finally
        {
            if (Directory.Exists(pluginRoot))
                Directory.Delete(pluginRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PrivateStorage_SnapshotReplacementUsesProjectedSizeAtQuotaBoundary()
    {
        var small = new SampleConfig("a", 1);
        var boundary = new SampleConfig(new string('x', 32), 2);
        var oversized = new SampleConfig(new string('x', 33), 3);
        var quota = JsonSerializer.SerializeToUtf8Bytes(
            boundary,
            PluginHostServicesJsonContext.Default.SampleConfig).LongLength;
        using var fixture = new StorageFixture(quota, maxFiles: 2);

        Assert.True((await fixture.Storage.WriteSnapshotAsync(
            "state.json",
            small,
            PluginHostServicesJsonContext.Default.SampleConfig,
            CancellationToken.None)).IsOk(out _));
        Assert.True((await fixture.Storage.WriteSnapshotAsync(
            "state.json",
            boundary,
            PluginHostServicesJsonContext.Default.SampleConfig,
            CancellationToken.None)).IsOk(out _));
        Assert.Equal(quota, new FileInfo(Path.Combine(fixture.DataRoot, "state.json")).Length);

        var rejected = await fixture.Storage.WriteSnapshotAsync(
            "state.json",
            oversized,
            PluginHostServicesJsonContext.Default.SampleConfig,
            CancellationToken.None);
        Assert.True(rejected.IsErr(out var quotaError));
        Assert.Equal("plugin_storage_byte_quota", quotaError!.Code);
        Assert.Equal(quota, new FileInfo(Path.Combine(fixture.DataRoot, "state.json")).Length);

        Assert.True((await fixture.Storage.WriteSnapshotAsync(
            "state.json",
            small,
            PluginHostServicesJsonContext.Default.SampleConfig,
            CancellationToken.None)).IsOk(out _));
        Assert.Equal(
            JsonSerializer.SerializeToUtf8Bytes(small, PluginHostServicesJsonContext.Default.SampleConfig).LongLength,
            new FileInfo(Path.Combine(fixture.DataRoot, "state.json")).Length);
        Assert.Empty(Directory.GetFiles(fixture.DataRoot, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task PrivateStorage_RepeatedJsonlAppendStopsAtExactByteQuota()
    {
        var value = new SampleConfig("line", 1);
        var lineBytes = JsonSerializer.SerializeToUtf8Bytes(
            value,
            PluginHostServicesJsonContext.Default.SampleConfig).LongLength + 1;
        using var fixture = new StorageFixture(lineBytes * 3, maxFiles: 1);

        for (var index = 0; index < 3; index++)
        {
            var result = await fixture.Storage.AppendJsonlAsync(
                "events.jsonl",
                value,
                PluginHostServicesJsonContext.Default.SampleConfig,
                CancellationToken.None);
            Assert.True(result.IsOk(out _));
        }

        var rejected = await fixture.Storage.AppendJsonlAsync(
            "events.jsonl",
            value,
            PluginHostServicesJsonContext.Default.SampleConfig,
            CancellationToken.None);
        Assert.True(rejected.IsErr(out var quotaError));
        Assert.Equal("plugin_storage_byte_quota", quotaError!.Code);

        var path = Path.Combine(fixture.DataRoot, "events.jsonl");
        Assert.Equal(lineBytes * 3, new FileInfo(path).Length);
        Assert.Equal(3, File.ReadAllLines(path).Length);
    }

    [Fact]
    public async Task PrivateStorage_ConcurrentJsonlAppendCannotOvercommitOrCorruptLines()
    {
        var value = new SampleConfig("concurrent", 7);
        var lineBytes = JsonSerializer.SerializeToUtf8Bytes(
            value,
            PluginHostServicesJsonContext.Default.SampleConfig).LongLength + 1;
        const int acceptedLines = 16;
        using var fixture = new StorageFixture(lineBytes * acceptedLines, maxFiles: 1);

        var attempts = Enumerable.Range(0, acceptedLines * 4)
            .Select(_ => Task.Run(async () => await fixture.Storage.AppendJsonlAsync(
                "events.jsonl",
                value,
                PluginHostServicesJsonContext.Default.SampleConfig,
                CancellationToken.None)))
            .ToArray();
        var results = await Task.WhenAll(attempts);

        Assert.Equal(acceptedLines, results.Count(static result => result.IsOk(out _)));
        Assert.All(
            results.Where(static result => result.IsErr(out _)),
            result =>
            {
                Assert.True(result.IsErr(out var error));
                Assert.Equal("plugin_storage_byte_quota", error!.Code);
            });

        var path = Path.Combine(fixture.DataRoot, "events.jsonl");
        Assert.Equal(lineBytes * acceptedLines, new FileInfo(path).Length);
        var lines = File.ReadAllLines(path);
        Assert.Equal(acceptedLines, lines.Length);
        Assert.All(lines, line =>
        {
            var parsed = JsonSerializer.Deserialize(
                line,
                PluginHostServicesJsonContext.Default.SampleConfig);
            Assert.Equal(value, parsed);
        });
    }

    [Fact]
    public async Task PrivateStorage_MaxFileQuotaAndPathEscapeRemainEnforced()
    {
        using var fixture = new StorageFixture(quotaBytes: 4096, maxFiles: 2);
        var value = new SampleConfig("file", 1);

        Assert.True((await fixture.Storage.WriteSnapshotAsync(
            "a.json", value, PluginHostServicesJsonContext.Default.SampleConfig, CancellationToken.None)).IsOk(out _));
        Assert.True((await fixture.Storage.WriteSnapshotAsync(
            "nested/b.json", value, PluginHostServicesJsonContext.Default.SampleConfig, CancellationToken.None)).IsOk(out _));

        var third = await fixture.Storage.WriteSnapshotAsync(
            "c.json", value, PluginHostServicesJsonContext.Default.SampleConfig, CancellationToken.None);
        Assert.True(third.IsErr(out var fileError));
        Assert.Equal("plugin_storage_file_quota", fileError!.Code);

        var escaped = await fixture.Storage.WriteSnapshotAsync(
            "../escape.json", value, PluginHostServicesJsonContext.Default.SampleConfig, CancellationToken.None);
        Assert.True(escaped.IsErr(out var pathError));
        Assert.Equal("plugin_storage_path_invalid", pathError!.Code);
    }

    [Fact]
    public void HttpEndpointPolicy_RejectsNonLoopbackAndPortConflicts()
    {
        var registry = new PluginHttpEndpointRegistry();
        var identity = new PluginIdentity("fixture.http", "1.0.0");
        var policy = new PluginHttpEndpointPolicy(identity.Id, registry, new PluginErrorFactory(identity));

        var ok = policy.ValidateAndRegister("127.0.0.1", 18080);
        Assert.True(ok.IsOk(out _));

        var other = new PluginHttpEndpointPolicy(
            "fixture.http.other",
            registry,
            new PluginErrorFactory(new PluginIdentity("fixture.http.other", "1.0.0")));
        var conflict = other.ValidateAndRegister("127.0.0.1", 18080);
        Assert.True(conflict.IsErr(out var conflictError));
        Assert.Equal("plugin_http_port_conflict", conflictError!.Code);

        var publicBind = policy.ValidateAndRegister("8.8.8.8", 18081);
        Assert.True(publicBind.IsErr(out var publicError));
        Assert.Equal("plugin_http_bind_not_loopback", publicError!.Code);

        var wildcard = policy.ValidateAndRegister("0.0.0.0", 18082);
        Assert.True(wildcard.IsErr(out var wildcardError));
        Assert.Equal("plugin_http_bind_not_loopback", wildcardError!.Code);
    }

    [Fact]
    public void HttpEndpointPolicy_ReleasesOnlyTheRequestedOwnedEndpoint()
    {
        var registry = new PluginHttpEndpointRegistry();
        var identity = new PluginIdentity("fixture.http.owner", "1.0.0");
        var owner = new PluginHttpEndpointPolicy(identity.Id, registry, new PluginErrorFactory(identity));
        Assert.True(owner.ValidateAndRegister("127.0.0.1", 18080).IsOk(out _));
        Assert.True(owner.ValidateAndRegister("127.0.0.1", 18081).IsOk(out _));

        owner.Release("127.0.0.1", 18080);

        var otherIdentity = new PluginIdentity("fixture.http.other-owner", "1.0.0");
        var other = new PluginHttpEndpointPolicy(
            otherIdentity.Id,
            registry,
            new PluginErrorFactory(otherIdentity));
        Assert.True(other.ValidateAndRegister("127.0.0.1", 18080).IsOk(out _));
        var stillOwned = other.ValidateAndRegister("127.0.0.1", 18081);
        Assert.True(stillOwned.IsErr(out var conflict));
        Assert.Equal("plugin_http_port_conflict", conflict!.Code);

        owner.ReleaseAll();
        Assert.True(other.ValidateAndRegister("127.0.0.1", 18081).IsOk(out _));
    }

    [Fact]
    public async Task Authentication_VerifiesMainTokenAndJwtAudience()
    {
        var identity = new PluginIdentity("fixture.auth", "1.0.0");
        var auth = new PluginAuthentication(new PluginErrorFactory(identity));
        var audience = "http://127.0.0.1:11453/mcp";

        var main = await auth.VerifyAsync(
            AppConfig.Get().MainToken,
            audience,
            new PluginAuthenticationOptions(AllowMainToken: true),
            CancellationToken.None);
        Assert.True(main.IsOk(out var mainPrincipal));
        Assert.True(mainPrincipal!.IsMainToken);
        Assert.Equal("daemon-main", mainPrincipal.Subject);
        Assert.Contains("*", mainPrincipal.Permissions);

        var rejectedMain = await auth.VerifyAsync(
            AppConfig.Get().MainToken,
            audience,
            new PluginAuthenticationOptions(AllowMainToken: false),
            CancellationToken.None);
        Assert.True(rejectedMain.IsErr(out _));

        // Legacy JwtUtils tokens carry aud=MCServerLauncher.Daemon; dual-accept only
        // when expectedAudience is the V2 API canonical URI.
        var apiAudience = AppConfig.Get().Security.ApiCanonicalUri;
        var jwt = JwtUtils.GenerateToken("mcsl.instance.catalog.get", 3600);
        var jwtResult = await auth.VerifyAsync(
            jwt,
            apiAudience,
            new PluginAuthenticationOptions(AllowMainToken: false),
            CancellationToken.None);
        Assert.True(jwtResult.IsOk(out var jwtPrincipal));
        Assert.False(jwtPrincipal!.IsMainToken);
        Assert.Contains("mcsl.instance.catalog.get", jwtPrincipal.Permissions);

        // MCP audience must NOT dual-accept legacy aud=MCServerLauncher.Daemon.
        var mcpAudience = "http://127.0.0.1:11453/mcp";
        var mcpRejected = await auth.VerifyAsync(
            jwt,
            mcpAudience,
            new PluginAuthenticationOptions(AllowMainToken: false),
            CancellationToken.None);
        Assert.True(mcpRejected.IsErr(out _));
    }

    public sealed record SampleConfig(string Name, int Count);

    private sealed class StorageFixture : IDisposable
    {
        private readonly string _pluginRoot;

        internal StorageFixture(long quotaBytes, int maxFiles)
        {
            var identity = new PluginIdentity($"fixture.storage.{Guid.NewGuid():N}", "1.0.0");
            _pluginRoot = Path.Combine(FileManager.Root, "plugins", identity.Id);
            DataRoot = Path.Combine(_pluginRoot, "data");
            Storage = new PluginPrivateStorage(
                identity,
                new DaemonPluginsConfig
                {
                    Storage = new PluginStorageConfig
                    {
                        DefaultQuotaBytes = quotaBytes,
                        DefaultMaxFiles = maxFiles,
                    },
                },
                new PluginErrorFactory(identity));
        }

        internal string DataRoot { get; }

        internal PluginPrivateStorage Storage { get; }

        public void Dispose()
        {
            if (Directory.Exists(_pluginRoot))
                Directory.Delete(_pluginRoot, recursive: true);
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(PluginHostServicesTests.SampleConfig))]
internal partial class PluginHostServicesJsonContext : JsonSerializerContext;
