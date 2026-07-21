using MCServerLauncher.Common.Contracts.Auth;
using System.Collections.Immutable;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Application;

/// <summary>
/// Permission-bearing caller context for Host and ForPrincipal application paths.
/// Public application methods do not take this as a parameter; authorized proxies hold it.
/// </summary>
public interface ICallerContext
{
    string Subject { get; }

    ImmutableArray<string> Permissions { get; }

    bool IsMainToken { get; }

    bool HasPermission(string methodPermission);

    Result<Unit, DaemonError> EnsurePermission(string methodPermission);
}

/// <summary>
/// Factory for Host and user-principal caller contexts.
/// </summary>
public interface ICallerContextFactory
{
    /// <summary>
    /// Builds a Host principal from the union of methods of granted implemented features.
    /// Host is not automatically `*`.
    /// </summary>
    ICallerContext CreateHost(IEnumerable<string> grantedFeatureIds);

    /// <summary>
    /// Builds a user principal view from a verified token principal.
    /// </summary>
    ICallerContext ForPrincipal(VerifiedPrincipal principal);
}

/// <summary>
/// Application surface for main-token JWT issuance.
/// Not a plugin feature; only the main token may call the issue RPC.
/// </summary>
public interface ITokenIssueApplication
{
    Task<Result<TokenIssueResult, DaemonError>> IssueTokenAsync(
        TokenIssueRequest request,
        ICallerContext caller,
        CancellationToken cancellationToken);
}
