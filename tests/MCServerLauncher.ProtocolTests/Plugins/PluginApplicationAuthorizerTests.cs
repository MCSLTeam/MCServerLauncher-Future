using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.ApplicationCore.Auth;
using MCServerLauncher.Daemon.Plugins;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests;

public sealed class PluginApplicationAuthorizerTests
{
    [Fact]
    public async Task ExposesOnlyFeatureGatedAuthorizedApplications()
    {
        var inner = new RecordingInstanceQueries();
        var undeclaredControl = new RecordingOperationControl();
        var verifiedPrincipals = new VerifiedPrincipalAuthority();
        var identity = new PluginIdentity("community.authorizer-test", "1.0.0");
        var authorizer = new PluginApplicationAuthorizer(
            identity,
            ["instance.query"],
            new CallerContextFactory(verifiedPrincipals),
            instanceCatalog: null,
            instanceQueries: inner,
            system: null,
            instanceManagement: null,
            operationQueries: null,
            operationControl: undeclaredControl,
            provisioning: null);

        var hostQueries = Assert.IsType<AuthorizedInstanceQueryApplication>(authorizer.Host.InstanceQueries);
        Assert.NotSame(inner, hostQueries);
        Assert.Equal("plugin:community.authorizer-test", authorizer.Host.Caller.Subject);
        Assert.Throws<InvalidOperationException>(() => authorizer.Host.OperationControl);

        var hostResult = await hostQueries.ListInstanceReportsAsync(CancellationToken.None);
        Assert.True(hostResult.IsErr(out var hostError));
        Assert.Equal("test.inner", hostError!.Code);
        Assert.Equal(1, inner.CallCount);

        var principal = new VerifiedPrincipal(
            "user-without-permissions",
            "token-id",
            "issuer",
            "audience",
            DateTimeOffset.UtcNow.AddMinutes(5),
            ImmutableArray<string>.Empty,
            isMainToken: false);
        var principalApplications = authorizer.ForPrincipal(verifiedPrincipals.Register(identity, principal));

        var denied = await principalApplications.InstanceQueries.ListInstanceReportsAsync(CancellationToken.None);
        Assert.True(denied.IsErr(out var deniedError));
        Assert.Equal("auth.permission_denied", deniedError!.Code);
        Assert.Equal(1, inner.CallCount);

        var cancelPrincipal = new VerifiedPrincipal(
            "user-with-cancel-permission",
            "cancel-token-id",
            "issuer",
            "audience",
            DateTimeOffset.UtcNow.AddMinutes(5),
            ImmutableArray.Create("mcsl.operation.cancel"),
            isMainToken: false);
        var cancelApplications = authorizer.ForPrincipal(verifiedPrincipals.Register(identity, cancelPrincipal));
        Assert.Throws<InvalidOperationException>(() => cancelApplications.OperationControl);
        Assert.Equal(0, undeclaredControl.CallCount);
    }

    [Fact]
    public async Task CachedPrincipalApplicationsRejectInvocationAtExactExpiry()
    {
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time = new ManualTimeProvider(now);
        var identity = new PluginIdentity("community.expiring-principal", "1.0.0");
        var verifiedPrincipals = new VerifiedPrincipalAuthority();
        var inner = new RecordingInstanceQueries();
        var authorizer = new PluginApplicationAuthorizer(
            identity,
            ["instance.query"],
            new CallerContextFactory(verifiedPrincipals, time),
            instanceCatalog: null,
            instanceQueries: inner,
            system: null,
            instanceManagement: null,
            operationQueries: null,
            operationControl: null,
            provisioning: null);
        var principal = new VerifiedPrincipal(
            "expiring-user",
            "expiring-token-id",
            "issuer",
            "audience",
            now.AddMinutes(5),
            ImmutableArray.Create("mcsl.instance.report.list"),
            isMainToken: false);
        var applications = authorizer.ForPrincipal(verifiedPrincipals.Register(identity, principal));

        var beforeExpiry = await applications.InstanceQueries.ListInstanceReportsAsync(CancellationToken.None);
        Assert.True(beforeExpiry.IsErr(out var innerError));
        Assert.Equal("test.inner", innerError!.Code);
        Assert.Equal(1, inner.CallCount);

        time.Advance(TimeSpan.FromMinutes(5));
        var atExpiry = await applications.InstanceQueries.ListInstanceReportsAsync(CancellationToken.None);

        Assert.True(atExpiry.IsErr(out var expiredError));
        Assert.Equal("auth.principal_expired", expiredError!.Code);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public void ForPrincipalRejectsPrincipalVerifiedForAnotherPlugin()
    {
        var pluginA = new PluginIdentity("community.principal-owner-a", "1.0.0");
        var pluginB = new PluginIdentity("community.principal-owner-b", "1.0.0");
        var verifiedPrincipals = new VerifiedPrincipalAuthority();
        var principal = new VerifiedPrincipal(
            "user-a",
            "token-a",
            "issuer",
            "audience-a",
            DateTimeOffset.UtcNow.AddMinutes(5),
            ImmutableArray.Create("mcsl.instance.report.list"),
            isMainToken: false);
        verifiedPrincipals.Register(pluginA, principal);
        var authorizer = new PluginApplicationAuthorizer(
            pluginB,
            [],
            new CallerContextFactory(verifiedPrincipals),
            instanceCatalog: null,
            instanceQueries: null,
            system: null,
            instanceManagement: null,
            operationQueries: null,
            operationControl: null,
            provisioning: null);

        Assert.Throws<ArgumentException>(() => authorizer.ForPrincipal(principal));
    }

    [Fact]
    public void RegistrationRejectsAudienceDifferentFromVerifiedPrincipal()
    {
        var identity = new PluginIdentity("community.audience-owner", "1.0.0");
        var verifiedPrincipals = new VerifiedPrincipalAuthority();
        var principal = new VerifiedPrincipal(
            "user-a",
            "token-a",
            "issuer",
            "audience-a",
            DateTimeOffset.UtcNow.AddMinutes(5),
            ImmutableArray<string>.Empty,
            isMainToken: false);

        Assert.Throws<ArgumentException>(() =>
            verifiedPrincipals.Register(identity, "audience-b", principal));
    }

    [Fact]
    public void ForPrincipalRejectsUnregisteredFriendConstructedPrincipal()
    {
        var verifiedPrincipals = new VerifiedPrincipalAuthority();
        var factory = new CallerContextFactory(verifiedPrincipals)
            .ForPlugin(new PluginIdentity("community.forged-principal", "1.0.0"));
        var forged = new VerifiedPrincipal(
            "forged-user",
            "forged-token",
            "MCServerLauncher.Daemon",
            "mcsl://daemon/api/v2",
            DateTimeOffset.UtcNow.AddMinutes(5),
            ImmutableArray.Create("mcsl.operation.cancel"),
            isMainToken: false);

        Assert.Throws<ArgumentException>(() => factory.ForPrincipal(forged));

        var forgedMain = new VerifiedPrincipal(
            PrincipalIdentityPolicy.MainTokenSubject,
            "forged-main-token",
            "MCServerLauncher.Daemon",
            "mcsl://daemon/api/v2",
            DateTimeOffset.MaxValue,
            ImmutableArray.Create(PrincipalIdentityPolicy.GlobalOwnerPrincipal),
            isMainToken: true);
        Assert.Throws<ArgumentException>(() => factory.ForPrincipal(forgedMain));
    }

    private sealed class RecordingInstanceQueries : IInstanceQueryApplication
    {
        public int CallCount { get; private set; }

        public Task<Result<InstanceReport, DaemonError>> GetInstanceReportAsync(
            InstanceReference request,
            CancellationToken cancellationToken) =>
            Invoked<InstanceReport>();

        public Task<Result<InstanceReportList, DaemonError>> ListInstanceReportsAsync(
            CancellationToken cancellationToken) =>
            Invoked<InstanceReportList>();

        public Task<Result<InstanceLogResult, DaemonError>> GetInstanceLogAsync(
            InstanceLogQuery request,
            CancellationToken cancellationToken) =>
            Invoked<InstanceLogResult>();

        public Task<Result<InstanceSettingsResult, DaemonError>> GetInstanceSettingsAsync(
            InstanceReference request,
            CancellationToken cancellationToken) =>
            Invoked<InstanceSettingsResult>();

        private Task<Result<T, DaemonError>> Invoked<T>()
            where T : notnull
        {
            CallCount++;
            return Task.FromResult(Result.Err<T, DaemonError>(
                new ValidationDaemonError("test.inner", "The inner application was invoked.")));
        }
    }

    private sealed class RecordingOperationControl : IOperationControlApplication
    {
        public int CallCount { get; private set; }

        public Task<Result<OperationCancelResult, DaemonError>> CancelOperationAsync(
            OperationCancelRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Result.Err<OperationCancelResult, DaemonError>(
                new ValidationDaemonError("test.inner", "The inner application was invoked.")));
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        internal void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }
}
