using System.Collections.Immutable;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MCServerLauncher.Common.Contracts.Auth;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.ApplicationCore.Auth;
using MCServerLauncher.Daemon.ApplicationCore.Operations;
using MCServerLauncher.Daemon.Plugins;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;
using Microsoft.IdentityModel.Tokens;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests;

public sealed class PrincipalOwnerIsolationTests
{
    [Theory]
    [InlineData("*")]
    [InlineData("daemon-main")]
    [InlineData("plugin:community.owner")]
    public async Task TokenIssueRejectsDaemonOwnedSubjects(string subject)
    {
        var application = new TokenIssueApplication();
        var main = new CallerContext(
            PrincipalIdentityPolicy.MainTokenSubject,
            [PrincipalIdentityPolicy.GlobalOwnerPrincipal],
            isMainToken: true);

        var result = await application.IssueTokenAsync(
            new TokenIssueRequest(
                subject,
                ApiAudience(),
                ["mcsl.operation.list"],
                TtlSeconds: 60),
            main,
            CancellationToken.None);

        Assert.True(result.IsErr(out var error));
        Assert.Equal("auth.token_issue.subject_reserved", error!.Code);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("daemon-main")]
    [InlineData("plugin:community.owner")]
    public async Task SignedJwtWithDaemonOwnedSubjectIsRejectedByV2AndPluginAuth(string subject)
    {
        var token = CreateSignedToken(subject);
        Assert.True(JwtUtils.ValidateToken(token));

        Assert.False(TouchSocketV2TransportPlugin.TryAuthenticateToken(
            token,
            TimeProvider.System,
            out _));

        var identity = new PluginIdentity("community.auth-boundary", "1.0.0");
        var authentication = new PluginAuthentication(new PluginErrorFactory(identity));
        var pluginResult = await authentication.VerifyAsync(
            token,
            ApiAudience(),
            new PluginAuthenticationOptions(AllowMainToken: false),
            CancellationToken.None);

        Assert.True(pluginResult.IsErr(out var pluginError));
        Assert.Equal("plugin_auth_invalid", pluginError!.Code);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("daemon-main")]
    [InlineData("plugin:community.owner")]
    public async Task V2OperationAndProvisioningBindingsRejectReservedNonMainSubjects(string subject)
    {
        var operations = new RecordingOperationApplication();
        var provisioning = new RecordingProvisioningApplication();
        var catalog = CreateOwnershipCatalog(operations, provisioning);
        var context = new ProtocolInvocationContext(
            ProtocolExecutionOwner.BuiltIn,
            new TestPermissionView(
                ["mcsl.operation.list", "mcsl.provisioning.resolve"],
                subject,
                IsMainToken: false));

        var operation = await Invoke<OperationListQuery, OperationListResult>(
            catalog,
            "mcsl.operation.list",
            context,
            new OperationListQuery("spoofed-owner"));
        var plan = await Invoke<ProvisioningResolveRequest, ProvisioningPlanSnapshot>(
            catalog,
            "mcsl.provisioning.resolve",
            context,
            ResolveRequest("spoofed-owner"));

        Assert.True(operation.Result.IsErr(out var operationError));
        Assert.Equal("auth.subject_reserved", operationError!.Code);
        Assert.True(plan.Result.IsErr(out var planError));
        Assert.Equal("auth.subject_reserved", planError!.Code);
        Assert.Equal(0, operations.ListCallCount);
        Assert.Equal(0, provisioning.ResolveCallCount);
    }

    [Fact]
    public async Task V2OwnershipBindingsUseOnlyAuthenticatedSubjectAndRawMainIdentity()
    {
        var operations = new RecordingOperationApplication();
        var provisioning = new RecordingProvisioningApplication();
        var catalog = CreateOwnershipCatalog(operations, provisioning);
        var ownerContext = new ProtocolInvocationContext(
            ProtocolExecutionOwner.BuiltIn,
            new TestPermissionView(
                ["mcsl.operation.list", "mcsl.provisioning.resolve"],
                "owner-a",
                IsMainToken: false));

        _ = await Invoke<OperationListQuery, OperationListResult>(
            catalog,
            "mcsl.operation.list",
            ownerContext,
            new OperationListQuery("owner-b"));
        _ = await Invoke<ProvisioningResolveRequest, ProvisioningPlanSnapshot>(
            catalog,
            "mcsl.provisioning.resolve",
            ownerContext,
            ResolveRequest("owner-b"));

        Assert.Equal("owner-a", operations.ListOwner);
        Assert.Equal("owner-a", provisioning.CreatorPrincipal);

        var mainContext = new ProtocolInvocationContext(
            ProtocolExecutionOwner.BuiltIn,
            new TestPermissionView(
                [PrincipalIdentityPolicy.GlobalOwnerPrincipal],
                PrincipalIdentityPolicy.MainTokenSubject,
                IsMainToken: true));
        _ = await Invoke<OperationListQuery, OperationListResult>(
            catalog,
            "mcsl.operation.list",
            mainContext,
            new OperationListQuery("owner-b"));
        _ = await Invoke<ProvisioningResolveRequest, ProvisioningPlanSnapshot>(
            catalog,
            "mcsl.provisioning.resolve",
            mainContext,
            ResolveRequest("owner-b"));

        Assert.Equal(PrincipalIdentityPolicy.GlobalOwnerPrincipal, operations.ListOwner);
        Assert.Equal(PrincipalIdentityPolicy.MainTokenSubject, provisioning.CreatorPrincipal);

        var forgedMainContext = new ProtocolInvocationContext(
            ProtocolExecutionOwner.BuiltIn,
            new TestPermissionView(
                [PrincipalIdentityPolicy.GlobalOwnerPrincipal],
                "owner-a",
                IsMainToken: true));
        var forged = await Invoke<OperationListQuery, OperationListResult>(
            catalog,
            "mcsl.operation.list",
            forgedMainContext,
            new OperationListQuery());
        Assert.True(forged.Result.IsErr(out var forgedError));
        Assert.Equal("auth.subject_invalid", forgedError!.Code);
    }

    [Fact]
    public async Task ForPrincipalKeepsForeignOperationsPrivateWhileRawMainSeesAll()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-principal-owner-").FullName;
        try
        {
            await using var coordinator = new OperationCoordinator(rootDirectory: root);
            var verifiedPrincipals = new VerifiedPrincipalAuthority();
            var callerContexts = new CallerContextFactory(verifiedPrincipals);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = await coordinator.StartAsync(
                kind: "test.owner-isolation",
                target: "target-a",
                ownerPrincipal: "owner-a",
                executor: async (_, _, cancellationToken) =>
                {
                    entered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return Result.Ok<string, DaemonError>("unreachable");
                },
                cancellationToken: CancellationToken.None);
            Assert.True(started.IsOk(out var accepted));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var pluginIdentity = new PluginIdentity("community.owner-isolation", "1.0.0");
            var authorizer = new PluginApplicationAuthorizer(
                pluginIdentity,
                ["operation.query", "operation.cancel"],
                callerContexts,
                instanceCatalog: null,
                instanceQueries: null,
                system: null,
                instanceManagement: null,
                operationQueries: coordinator,
                operationControl: coordinator,
                provisioning: null);
            var foreign = authorizer.ForPrincipal(verifiedPrincipals.Register(pluginIdentity, CreatePrincipal(
                "owner-b",
                ["mcsl.operation.list", "mcsl.operation.get", "mcsl.operation.cancel"])));

            var foreignList = await foreign.OperationQueries.ListOperationsAsync(
                new OperationListQuery(PrincipalIdentityPolicy.GlobalOwnerPrincipal),
                CancellationToken.None);
            Assert.True(foreignList.IsOk(out var foreignItems));
            Assert.Empty(foreignItems!.Operations);

            var foreignGet = await foreign.OperationQueries.GetOperationAsync(
                new OperationReference(accepted!.OperationId, "owner-a"),
                CancellationToken.None);
            Assert.True(foreignGet.IsErr(out var getError));
            Assert.Equal("operation.forbidden", getError!.Code);

            var foreignCancel = await foreign.OperationControl.CancelOperationAsync(
                new OperationCancelRequest(accepted.OperationId, "owner-a"),
                CancellationToken.None);
            Assert.True(foreignCancel.IsErr(out var cancelError));
            Assert.Equal("operation.forbidden", cancelError!.Code);

            var authentication = new PluginAuthentication(
                new PluginErrorFactory(pluginIdentity),
                verifiedPrincipals);
            var verifiedMain = await authentication.VerifyAsync(
                AppConfig.Get().MainToken,
                ApiAudience(),
                new PluginAuthenticationOptions(AllowMainToken: true),
                CancellationToken.None);
            Assert.True(verifiedMain.IsOk(out var mainPrincipal));
            var main = authorizer.ForPrincipal(mainPrincipal!);

            var mainList = await main.OperationQueries.ListOperationsAsync(
                new OperationListQuery("owner-b"),
                CancellationToken.None);
            Assert.True(mainList.IsOk(out var allItems));
            Assert.Contains(allItems!.Operations, item => item.OperationId == accepted.OperationId);

            var mainCancel = await main.OperationControl.CancelOperationAsync(
                new OperationCancelRequest(accepted.OperationId, "owner-b"),
                CancellationToken.None);
            Assert.True(mainCancel.IsOk(out var cancellation));
            Assert.True(cancellation!.CancelRequested);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ForPrincipalOverwritesSpoofedProvisioningSubjects()
    {
        var provisioning = new RecordingProvisioningApplication();
        var verifiedPrincipals = new VerifiedPrincipalAuthority();
        var pluginIdentity = new PluginIdentity("community.provisioning-owner", "1.0.0");
        var authorizer = new PluginApplicationAuthorizer(
            pluginIdentity,
            ["provisioning.manage"],
            new CallerContextFactory(verifiedPrincipals),
            instanceCatalog: null,
            instanceQueries: null,
            system: null,
            instanceManagement: null,
            operationQueries: null,
            operationControl: null,
            provisioning);
        var applications = authorizer.ForPrincipal(verifiedPrincipals.Register(pluginIdentity, CreatePrincipal(
            "owner-a",
            ["mcsl.provisioning.resolve", "mcsl.provisioning.get", "mcsl.provisioning.execute"])));
        var planId = Guid.NewGuid();

        _ = await applications.Provisioning.ResolveAsync(ResolveRequest("owner-b"), CancellationToken.None);
        _ = await applications.Provisioning.GetPlanAsync(
            new ProvisioningPlanReference(planId, "owner-b"),
            CancellationToken.None);
        _ = await applications.Provisioning.ExecuteAsync(
            new ProvisioningExecuteRequest(planId, "owner-b"),
            CancellationToken.None);

        Assert.Equal("owner-a", provisioning.CreatorPrincipal);
        Assert.Equal("owner-a", provisioning.GetOwnerPrincipal);
        Assert.Equal("owner-a", provisioning.ExecutorPrincipal);
    }

    private static string ApiAudience() =>
        string.IsNullOrWhiteSpace(AppConfig.Get().Security.ApiCanonicalUri)
            ? "mcsl://daemon/api/v2"
            : AppConfig.Get().Security.ApiCanonicalUri;

    private static string CreateSignedToken(string subject)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: "MCServerLauncher.Daemon",
            audience: ApiAudience(),
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, subject),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim("permissions", "mcsl.operation.list")
            ],
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AppConfig.Get().Secret)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static VerifiedPrincipal CreatePrincipal(
        string subject,
        ImmutableArray<string> permissions) =>
        new(
            subject,
            Guid.NewGuid().ToString("N"),
            "MCServerLauncher.Daemon",
            ApiAudience(),
            DateTimeOffset.UtcNow.AddMinutes(5),
            permissions,
            isMainToken: false);

    private static ProvisioningResolveRequest ResolveRequest(string creatorPrincipal) =>
        new(
            ProvisioningProviderKind.Vanilla,
            "owner-test",
            "1.21.8",
            Source: "server.jar",
            Mirror: InstanceFactoryMirror.None,
            JavaPath: "java",
            CreatorPrincipal: creatorPrincipal);

    private static FrozenProtocolCatalog CreateOwnershipCatalog(
        RecordingOperationApplication operations,
        RecordingProvisioningApplication provisioning)
    {
        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("Ownership tests", "1.0.0"));
        BuiltInOperationRpcRegistrar.Register(builder, operations);
        BuiltInProvisioningRpcRegistrar.Register(builder, provisioning);
        return builder.Freeze();
    }

    private static async Task<ProtocolRpcExecution<TResult>> Invoke<TRequest, TResult>(
        FrozenProtocolCatalog catalog,
        string method,
        ProtocolInvocationContext context,
        TRequest request)
        where TResult : notnull
    {
        var entry = catalog.Rpcs[new RpcMethod(method)];
        var binding = Assert.IsType<RpcBinding<TRequest, TResult>>(entry.Binding);
        return await binding.Handler(context, request, CancellationToken.None);
    }

    private sealed record TestPermissionView(
        ImmutableArray<string> Permissions,
        string Subject,
        bool IsMainToken) : IProtocolPermissionView;

    private sealed class RecordingOperationApplication : IOperationApplication
    {
        public int ListCallCount { get; private set; }
        public string? ListOwner { get; private set; }

        public Task<Result<OperationListResult, DaemonError>> ListOperationsAsync(
            OperationListQuery request,
            CancellationToken cancellationToken)
        {
            ListCallCount++;
            ListOwner = request.OwnerPrincipal;
            return Task.FromResult(Result.Ok<OperationListResult, DaemonError>(new OperationListResult([])));
        }

        public Task<Result<OperationSnapshot, DaemonError>> GetOperationAsync(
            OperationReference request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<OperationCancelResult, DaemonError>> CancelOperationAsync(
            OperationCancelRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingProvisioningApplication : IProvisioningApplication
    {
        public int ResolveCallCount { get; private set; }
        public string? CreatorPrincipal { get; private set; }
        public string? GetOwnerPrincipal { get; private set; }
        public string? ExecutorPrincipal { get; private set; }

        public Task<Result<ProvisioningPlanSnapshot, DaemonError>> ResolveAsync(
            ProvisioningResolveRequest request,
            CancellationToken cancellationToken)
        {
            ResolveCallCount++;
            CreatorPrincipal = request.CreatorPrincipal;
            return InnerError<ProvisioningPlanSnapshot>();
        }

        public Task<Result<ProvisioningPlanSnapshot, DaemonError>> GetPlanAsync(
            ProvisioningPlanReference request,
            CancellationToken cancellationToken)
        {
            GetOwnerPrincipal = request.OwnerPrincipal;
            return InnerError<ProvisioningPlanSnapshot>();
        }

        public Task<Result<ProvisioningExecuteResult, DaemonError>> ExecuteAsync(
            ProvisioningExecuteRequest request,
            CancellationToken cancellationToken)
        {
            ExecutorPrincipal = request.ExecutorPrincipal;
            return InnerError<ProvisioningExecuteResult>();
        }

        private static Task<Result<T, DaemonError>> InnerError<T>() where T : notnull =>
            Task.FromResult(Result.Err<T, DaemonError>(
                new ValidationDaemonError("test.inner", "The recording application was invoked.")));
    }
}
