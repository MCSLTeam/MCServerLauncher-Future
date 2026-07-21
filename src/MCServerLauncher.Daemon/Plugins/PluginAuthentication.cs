using System.Collections.Immutable;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
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

    internal PluginAuthentication(PluginErrorFactory errors)
    {
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
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
                Subject: "daemon-main",
                TokenId: "main",
                Issuer: "MCServerLauncher.Daemon",
                Audience: expectedAudience,
                ExpiresAt: DateTimeOffset.MaxValue,
                Permissions: ImmutableArray.Create("*"),
                IsMainToken: true);
            return Task.FromResult(Result.Ok<VerifiedPrincipal, DaemonError>(main));
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
            var tokenAud = jwt.Audiences.FirstOrDefault() ?? string.Empty;
            var apiCanonical = AppConfig.Get().Security.ApiCanonicalUri;
            var audienceOk =
                string.Equals(tokenAud, expectedAudience, StringComparison.Ordinal) ||
                (string.Equals(tokenAud, "MCServerLauncher.Daemon", StringComparison.Ordinal) &&
                 string.Equals(expectedAudience, apiCanonical, StringComparison.Ordinal));
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
            var subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst("sub")?.Value
                ?? "anonymous";
            var jti = jwt.Id;
            if (string.IsNullOrWhiteSpace(jti))
                jti = Guid.NewGuid().ToString("N");

            var verified = new VerifiedPrincipal(
                Subject: subject,
                TokenId: jti,
                Issuer: jwt.Issuer ?? "MCServerLauncher.Daemon",
                Audience: expectedAudience,
                ExpiresAt: new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero),
                Permissions: permissions,
                IsMainToken: false);
            return Task.FromResult(Result.Ok<VerifiedPrincipal, DaemonError>(verified));
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
