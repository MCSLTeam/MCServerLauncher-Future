using System.Collections.Immutable;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Plugins;

/// <summary>
/// Immutable principal returned by <see cref="IPluginAuthentication.VerifyAsync"/>.
/// Never contains the raw token or signing material.
/// </summary>
public sealed record VerifiedPrincipal(
    string Subject,
    string TokenId,
    string Issuer,
    string Audience,
    DateTimeOffset ExpiresAt,
    ImmutableArray<string> Permissions,
    bool IsMainToken);

/// <summary>
/// Options for audience-bound token verification.
/// </summary>
public sealed record PluginAuthenticationOptions(
    bool AllowMainToken = false);

/// <summary>
/// Verifies audience-bound daemon tokens into principals.
/// Feature: <c>auth.verify</c>.
/// </summary>
public interface IPluginAuthentication
{
    Task<Result<VerifiedPrincipal, DaemonError>> VerifyAsync(
        string token,
        string expectedAudience,
        PluginAuthenticationOptions options,
        CancellationToken cancellationToken);
}
