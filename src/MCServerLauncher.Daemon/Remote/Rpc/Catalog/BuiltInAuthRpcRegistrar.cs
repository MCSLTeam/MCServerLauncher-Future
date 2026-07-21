using MCServerLauncher.Common.Contracts.Auth;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.ApplicationCore.Auth;
using MCServerLauncher.Daemon.Remote.Authentication;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

internal static class BuiltInAuthRpcRegistrar
{
    public static void Register(ProtocolCatalogBuilder builder, ITokenIssueApplication tokenIssue)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(tokenIssue);

        var descriptor = (RpcDescriptor<TokenIssueRequest, TokenIssueResult>)BuiltInProtocolDefinitions.Rpcs.Single(
            candidate => StringComparer.Ordinal.Equals(candidate.Method.Value, "mcsl.auth.token.issue"));
        builder.RegisterBuiltInRpc(
            descriptor,
            new RpcBinding<TokenIssueRequest, TokenIssueResult>(
                ProtocolExecutionOwner.BuiltIn,
                async (context, request, token) =>
                {
                    var caller = CreateCaller(context);
                    var result = await tokenIssue.IssueTokenAsync(request, caller, token).ConfigureAwait(false);
                    return BuiltInApplicationRpcExecution.FromResult(result);
                }));
    }

    private static ICallerContext CreateCaller(ProtocolInvocationContext context)
    {
        var view = context.PermissionView;
        if (view is null)
        {
            return new CallerContext("anonymous", System.Collections.Immutable.ImmutableArray<string>.Empty, isMainToken: false);
        }

        return new CallerContext(
            subject: string.IsNullOrWhiteSpace(view.Subject) ? "anonymous" : view.Subject,
            permissions: view.Permissions,
            isMainToken: view.IsMainToken);
    }
}
