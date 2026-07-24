using System.Collections.Immutable;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Plugins;

/// <summary>
/// Immutable principal returned by <see cref="IPluginAuthentication.VerifyAsync"/>.
/// Never contains the raw token or signing material.
/// </summary>
public sealed class VerifiedPrincipal
{
    internal VerifiedPrincipal(
        string subject,
        string tokenId,
        string issuer,
        string audience,
        DateTimeOffset expiresAt,
        ImmutableArray<string> permissions,
        bool isMainToken)
    {
        if (permissions.IsDefault)
            throw new ArgumentException("Permissions cannot be default.", nameof(permissions));

        if (isMainToken)
        {
            PrincipalIdentityPolicy.ValidateMainTokenSubject(subject, nameof(subject));
            if (permissions.Length != 1 ||
                !string.Equals(permissions[0], PrincipalIdentityPolicy.GlobalOwnerPrincipal, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The main-token principal must have only the global permission.",
                    nameof(permissions));
            }
        }
        else
        {
            PrincipalIdentityPolicy.ValidateExternalSubject(subject, nameof(subject));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        Subject = subject;
        TokenId = tokenId;
        Issuer = issuer;
        Audience = audience;
        ExpiresAt = expiresAt;
        Permissions = permissions;
        IsMainToken = isMainToken;
    }

    public string Subject { get; }

    public string TokenId { get; }

    public string Issuer { get; }

    public string Audience { get; }

    public DateTimeOffset ExpiresAt { get; }

    public ImmutableArray<string> Permissions { get; }

    public bool IsMainToken { get; }
}

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
