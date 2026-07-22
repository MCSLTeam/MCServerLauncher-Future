using System.Collections.Immutable;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MCServerLauncher.Common.Contracts.Auth;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Remote.Authentication;
using Microsoft.IdentityModel.Tokens;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore.Auth;

internal sealed class TokenIssueApplication : ITokenIssueApplication
{
    public Task<Result<TokenIssueResult, DaemonError>> IssueTokenAsync(
        TokenIssueRequest request,
        ICallerContext caller,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);

        var config = AppConfig.Get();
        if (!config.Security.AllowMainTokenIssue)
        {
            return Task.FromResult(Result.Err<TokenIssueResult, DaemonError>(
                new PermissionDaemonError(
                    "auth.token_issue.disabled",
                    "Main-token JWT issuance is disabled by security.allow_main_token_issue.")));
        }

        // Spec: issue is main-token only (raw main path). Do not treat bare "*" as issue authority.
        if (!caller.IsMainToken)
        {
            return Task.FromResult(Result.Err<TokenIssueResult, DaemonError>(
                new PermissionDaemonError(
                    "auth.token_issue.main_token_only",
                    "Only the main token may issue JWTs.")));
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            return Task.FromResult(Result.Err<TokenIssueResult, DaemonError>(
                new ValidationDaemonError("auth.token_issue.subject_required", "Token subject is required.")));
        }

        if (!PrincipalIdentityPolicy.IsValidExternalSubject(request.Subject))
        {
            return Task.FromResult(Result.Err<TokenIssueResult, DaemonError>(
                new ValidationDaemonError(
                    "auth.token_issue.subject_reserved",
                    "Token subject is reserved for a daemon-owned identity.")));
        }

        if (string.IsNullOrWhiteSpace(request.Audience) ||
            !Uri.TryCreate(request.Audience, UriKind.Absolute, out _))
        {
            return Task.FromResult(Result.Err<TokenIssueResult, DaemonError>(
                new ValidationDaemonError(
                    "auth.token_issue.audience_invalid",
                    "Token audience must be an absolute URI.")));
        }

        if (request.TtlSeconds <= 0)
        {
            return Task.FromResult(Result.Err<TokenIssueResult, DaemonError>(
                new ValidationDaemonError("auth.token_issue.ttl_invalid", "Token TTL must be positive.")));
        }

        var maxTtl = Math.Max(1, config.Security.MaxTokenTtlSeconds);
        if (request.TtlSeconds > maxTtl)
        {
            return Task.FromResult(Result.Err<TokenIssueResult, DaemonError>(
                new ValidationDaemonError(
                    "auth.token_issue.ttl_exceeded",
                    $"Token TTL cannot exceed {maxTtl} seconds.")));
        }

        var permissions = request.Permissions.IsDefault
            ? ImmutableArray<string>.Empty
            : request.Permissions;
        if (permissions.IsDefaultOrEmpty)
        {
            return Task.FromResult(Result.Err<TokenIssueResult, DaemonError>(
                new ValidationDaemonError(
                    "auth.token_issue.permissions_required",
                    "Token permissions must be non-empty.")));
        }

        foreach (var permission in permissions)
        {
            if (string.IsNullOrWhiteSpace(permission) || !Permission.IsValid(permission) || permission == "*")
            {
                return Task.FromResult(Result.Err<TokenIssueResult, DaemonError>(
                    new ValidationDaemonError(
                        "auth.token_issue.permission_invalid",
                        permission == "*"
                            ? "Issued permissions must not include bare '*'."
                            : $"Permission '{permission}' is invalid.")));
            }

            if (!caller.HasPermission(permission))
            {
                return Task.FromResult(Result.Err<TokenIssueResult, DaemonError>(
                    new PermissionDaemonError(
                        "auth.token_issue.permission_subset",
                        "Issued permissions must be a subset of the caller's permissions.")));
            }
        }

        var permissionText = string.Join(',', permissions);
        if (!Permissions.IsValid(permissionText))
        {
            return Task.FromResult(Result.Err<TokenIssueResult, DaemonError>(
                new ValidationDaemonError(
                    "auth.token_issue.permission_invalid",
                    "Token permissions claim is invalid.")));
        }

        var now = DateTime.UtcNow;
        var expires = now.AddSeconds(request.TtlSeconds);
        var jti = Guid.NewGuid().ToString("N");
        var secretBytes = Encoding.UTF8.GetBytes(config.Secret);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.Subject),
            new(JwtRegisteredClaimNames.Jti, jti),
            new("permissions", permissionText),
        };

        var token = new JwtSecurityToken(
            issuer: "MCServerLauncher.Daemon",
            audience: request.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(secretBytes),
                SecurityAlgorithms.HmacSha256));

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        var result = new TokenIssueResult(
            Token: jwt,
            Subject: request.Subject,
            Audience: request.Audience,
            Permissions: permissions,
            ExpiresAt: new DateTimeOffset(expires, TimeSpan.Zero),
            TokenId: jti);
        return Task.FromResult(Result.Ok<TokenIssueResult, DaemonError>(result));
    }
}
