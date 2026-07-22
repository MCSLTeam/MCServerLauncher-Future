using System.Collections.Immutable;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.ApplicationCore.Auth;
using MCServerLauncher.Daemon.Remote.Authentication;
using Microsoft.IdentityModel.Tokens;
using RustyOptions;

namespace MCServerLauncher.Daemon.Plugins;

/// <summary>
/// Audience-bound token verification for plugins. Main-token superuser path is
/// opt-in via <see cref="PluginAuthenticationOptions.AllowMainToken"/>.
/// Never logs the raw token.
/// </summary>
internal sealed class PluginAuthentication : IPluginAuthentication
{
    private readonly PluginErrorFactory _errors;
    private readonly VerifiedPrincipalAuthority _verifiedPrincipals;

    internal PluginAuthentication(PluginErrorFactory errors)
        : this(errors, new VerifiedPrincipalAuthority())
    {
    }

    internal PluginAuthentication(
        PluginErrorFactory errors,
        VerifiedPrincipalAuthority verifiedPrincipals)
    {
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        _verifiedPrincipals = verifiedPrincipals ?? throw new ArgumentNullException(nameof(verifiedPrincipals));
    }

    public Task<Result<VerifiedPrincipal, DaemonError>> VerifyAsync(
        string token,
        string expectedAudience,
        PluginAuthenticationOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAudience);
        options ??= new PluginAuthenticationOptions();

        // Main-token superuser path (local convenience; audience is not required on the raw token).
        if (options.AllowMainToken &&
            string.Equals(token, AppConfig.Get().MainToken, StringComparison.Ordinal))
        {
            var main = new VerifiedPrincipal(
                subject: PrincipalIdentityPolicy.MainTokenSubject,
                tokenId: "main",
                issuer: "MCServerLauncher.Daemon",
                audience: expectedAudience,
                expiresAt: DateTimeOffset.MaxValue,
                permissions: ImmutableArray.Create(PrincipalIdentityPolicy.GlobalOwnerPrincipal),
                isMainToken: true);
            return Task.FromResult(Result.Ok<VerifiedPrincipal, DaemonError>(
                _verifiedPrincipals.Register(_errors.Identity, expectedAudience, main)));
        }

        try
        {
            var secretBytes = Encoding.UTF8.GetBytes(AppConfig.Get().Secret);
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                IssuerSigningKey = new SymmetricSecurityKey(secretBytes),
                ValidIssuer = "MCServerLauncher.Daemon",
                // Prefer the caller-supplied resource audience; dual-accept the legacy
                // MCServerLauncher.Daemon audience for one release window on V2 tokens
                // that are also used with MCP only when expectedAudience matches.
                ValidAudiences = new[] { expectedAudience, "MCServerLauncher.Daemon" },
                ClockSkew = TimeSpan.Zero,
            };

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, parameters, out var securityToken);
            if (securityToken is not JwtSecurityToken jwt)
            {
                return Task.FromResult(Result.Err<VerifiedPrincipal, DaemonError>(_errors.Create(
                    "plugin_auth_invalid",
                    "The bearer token is not a valid JWT.")));
            }

            // Enforce exact expected audience when the token carries the new resource aud.
            // Legacy aud=MCServerLauncher.Daemon is accepted only when expectedAudience is
            // the API canonical URI (V2 dual-accept). For MCP, expectedAudience is the
            // plugin canonical_uri and legacy aud must not pass unless it equals that.
            var tokenAudiences = jwt.Audiences.ToArray();
            var tokenAud = tokenAudiences.Length == 1
                ? tokenAudiences[0]
                : string.Empty;
            var apiCanonical = AppConfig.Get().Security.ApiCanonicalUri;
            var audienceOk =
                tokenAudiences.Length == 1 &&
                (string.Equals(tokenAud, expectedAudience, StringComparison.Ordinal) ||
                 (string.Equals(tokenAud, "MCServerLauncher.Daemon", StringComparison.Ordinal) &&
                  string.Equals(expectedAudience, apiCanonical, StringComparison.Ordinal)));
            if (!audienceOk)
            {
                return Task.FromResult(Result.Err<VerifiedPrincipal, DaemonError>(_errors.Create(
                    "plugin_auth_invalid",
                    "The bearer token audience is not valid for this resource.")));
            }

            var permissionsClaim = principal.FindFirst("permissions")?.Value;
            if (string.IsNullOrWhiteSpace(permissionsClaim) || !Permissions.IsValid(permissionsClaim))
            {
                return Task.FromResult(Result.Err<VerifiedPrincipal, DaemonError>(_errors.Create(
                    "plugin_auth_invalid",
                    "The bearer token permissions claim is missing or invalid.")));
            }

            var permissions = permissionsClaim
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToImmutableArray();
            var jti = jwt.Id;
            if (string.IsNullOrWhiteSpace(jti))
            {
                return Task.FromResult(Result.Err<VerifiedPrincipal, DaemonError>(_errors.Create(
                    "plugin_auth_invalid",
                    "The bearer token id is missing.")));
            }

            var subject = jwt.Subject;
            if (!PrincipalIdentityPolicy.IsValidExternalSubject(subject))
            {
                return Task.FromResult(Result.Err<VerifiedPrincipal, DaemonError>(_errors.Create(
                    "plugin_auth_invalid",
                    "The bearer token subject is missing or reserved.")));
            }

            var verified = new VerifiedPrincipal(
                subject: subject,
                tokenId: jti,
                issuer: jwt.Issuer ?? "MCServerLauncher.Daemon",
                audience: expectedAudience,
                expiresAt: new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero),
                permissions: permissions,
                isMainToken: false);
            return Task.FromResult(Result.Ok<VerifiedPrincipal, DaemonError>(
                _verifiedPrincipals.Register(_errors.Identity, expectedAudience, verified)));
        }
        catch (SecurityTokenException)
        {
            return Task.FromResult(Result.Err<VerifiedPrincipal, DaemonError>(_errors.Create(
                "plugin_auth_invalid",
                "The bearer token could not be validated.")));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return Task.FromResult(Result.Err<VerifiedPrincipal, DaemonError>(_errors.Create(
                "plugin_auth_invalid",
                "The bearer token could not be validated.")));
        }
    }
}
