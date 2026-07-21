using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Auth;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.ApplicationCore.Auth;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;

namespace MCServerLauncher.ProtocolTests;

public sealed class CallerContextAndTokenIssueTests
{
    [Fact]
    public void CallerContext_MatchesSegmentWildcardsAndMainToken()
    {
        var limited = new CallerContext(
            "user-a",
            ImmutableArray.Create("mcsl.instance.*", "mcsl.operation.get"),
            isMainToken: false);
        Assert.True(limited.HasPermission("mcsl.instance.start"));
        Assert.True(limited.HasPermission("mcsl.operation.get"));
        Assert.False(limited.HasPermission("mcsl.operation.cancel"));
        Assert.False(limited.HasPermission("mcsl.auth.token.issue"));

        var main = new CallerContext("daemon-main", ImmutableArray.Create("*"), isMainToken: true);
        Assert.True(main.HasPermission("mcsl.auth.token.issue"));
        Assert.True(main.HasPermission("mcsl.file.download.read"));

        // Bare "*" grant is not main identity.
        var starOnly = new CallerContext("star-user", ImmutableArray.Create("*"), isMainToken: false);
        Assert.False(starOnly.IsMainToken);
        Assert.True(starOnly.HasPermission("mcsl.instance.start"));
    }

    [Fact]
    public void HostFactory_ExpandsImplementedFeatureMethodsOnly()
    {
        var factory = new CallerContextFactory();
        var host = factory.CreateHost(["instance.manage", "operation.query", "backup.manage"]);
        Assert.Equal("plugin-host", host.Subject);
        Assert.True(host.HasPermission("mcsl.instance.start"));
        Assert.True(host.HasPermission("mcsl.operation.get"));
        Assert.False(host.HasPermission("mcsl.backup.create")); // unimplemented feature contributes nothing
        Assert.False(host.IsMainToken);
    }

    [Fact]
    public async Task TokenIssue_MainTokenOnlyWithSubsetAndTtlBounds()
    {
        var app = new TokenIssueApplication();
        var main = new CallerContext("daemon-main", ImmutableArray.Create("*"), isMainToken: true);

        var ok = await app.IssueTokenAsync(
            new TokenIssueRequest(
                Subject: "ci-bot",
                Audience: "mcsl://daemon/api/v2",
                Permissions: ImmutableArray.Create("mcsl.instance.start", "mcsl.instance.stop"),
                TtlSeconds: 3600),
            main,
            CancellationToken.None);
        Assert.True(ok.IsOk(out var issued));
        Assert.False(string.IsNullOrWhiteSpace(issued!.Token));
        Assert.Equal("ci-bot", issued.Subject);
        Assert.Equal("mcsl://daemon/api/v2", issued.Audience);
        Assert.Contains("mcsl.instance.start", issued.Permissions);

        // Issued JWT authenticates on V2 transport without elevating to main.
        Assert.True(JwtUtils.ValidateToken(issued.Token));
        Assert.True(TouchSocketV2TransportPlugin.TryAuthenticateToken(
            issued.Token,
            TimeProvider.System,
            out var verified));
        Assert.False(verified.IsMainToken);
        Assert.Equal("ci-bot", verified.Subject);
        Assert.Contains("mcsl.instance.start", verified.Permissions);

        // Non-main cannot issue (even with bare "*").
        var user = new CallerContext("user-a", ImmutableArray.Create("mcsl.instance.start"), isMainToken: false);
        var denied = await app.IssueTokenAsync(
            new TokenIssueRequest("x", "mcsl://daemon/api/v2", ImmutableArray.Create("mcsl.instance.start"), 60),
            user,
            CancellationToken.None);
        Assert.True(denied.IsErr(out _));

        var starPrincipal = new CallerContext("star-user", ImmutableArray.Create("*"), isMainToken: false);
        var starDenied = await app.IssueTokenAsync(
            new TokenIssueRequest("x", "mcsl://daemon/api/v2", ImmutableArray.Create("mcsl.instance.start"), 60),
            starPrincipal,
            CancellationToken.None);
        Assert.True(starDenied.IsErr(out _));

        // Bare "*" permission grant rejected.
        var starIssue = await app.IssueTokenAsync(
            new TokenIssueRequest("x", "mcsl://daemon/api/v2", ImmutableArray.Create("*"), 60),
            main,
            CancellationToken.None);
        Assert.True(starIssue.IsErr(out _));

        // Relative audience rejected.
        var badAud = await app.IssueTokenAsync(
            new TokenIssueRequest("x", "not-a-uri", ImmutableArray.Create("mcsl.instance.start"), 60),
            main,
            CancellationToken.None);
        Assert.True(badAud.IsErr(out _));

        // TTL above max rejected.
        var max = MCServerLauncher.Daemon.AppConfig.Get().Security.MaxTokenTtlSeconds;
        var badTtl = await app.IssueTokenAsync(
            new TokenIssueRequest("x", "mcsl://daemon/api/v2", ImmutableArray.Create("mcsl.instance.start"), max + 1),
            main,
            CancellationToken.None);
        Assert.True(badTtl.IsErr(out _));
    }

    [Fact]
    public void FeatureCatalog_InstanceManageIsImplementedForPreview1()
    {
        Assert.True(FeatureCatalog.IsImplemented(PluginFeature.InstanceManage));
        Assert.Contains("mcsl.instance.create", FeatureCatalog.MethodsFor(PluginFeature.InstanceManage));
    }

    [Fact]
    public async Task ConnectionOwner_DoesNotTreatStarPermissionAsMainToken()
    {
        await using var owner = new V2ConnectionOwner(new NoOpSender(), ["*"], subject: "star-user", isMainToken: false);
        Assert.False(owner.IsMainToken);
        Assert.Equal("star-user", owner.Subject);
        Assert.True(owner.CompiledPermissions.Matches(Permission.Of("mcsl.auth.token.issue")));
    }

    private sealed class NoOpSender : IV2OutboundSender
    {
        public ValueTask SendAsync(V2OutboundFrame frame, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask CloseAsync(V2ConnectionCloseReason reason, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
