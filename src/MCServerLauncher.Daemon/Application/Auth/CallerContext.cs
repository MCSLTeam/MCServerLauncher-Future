using System.Collections.Immutable;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Authentication;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore.Auth;

internal sealed class CallerContext : ICallerContext
{
    private readonly Permissions _compiled;

    public CallerContext(
        string subject,
        ImmutableArray<string> permissions,
        bool isMainToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        Subject = subject;
        Permissions = permissions.IsDefault
            ? ImmutableArray<string>.Empty
            : permissions;
        IsMainToken = isMainToken;
        _compiled = new Permissions(Permissions.ToArray());
    }

    public string Subject { get; }

    public ImmutableArray<string> Permissions { get; }

    public bool IsMainToken { get; }

    public bool HasPermission(string methodPermission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodPermission);
        if (IsMainToken || Permissions.Any(static p => p == "*"))
            return true;

        var required = Permission.Of(methodPermission);
        return _compiled.Matches(required);
    }

    public Result<Unit, DaemonError> EnsurePermission(string methodPermission)
    {
        if (HasPermission(methodPermission))
            return Result.Ok<Unit, DaemonError>(Unit.Default);

        return Result.Err<Unit, DaemonError>(
            new PermissionDaemonError(
                "auth.permission_denied",
                $"The caller is not permitted to invoke '{methodPermission}'."));
    }
}

internal sealed class CallerContextFactory : ICallerContextFactory
{
    public ICallerContext CreateHost(
        PluginIdentity plugin,
        IEnumerable<string> grantedFeatureIds)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(grantedFeatureIds);
        var methods = FeatureCatalog.ExpandMethodPermissions(grantedFeatureIds);
        return new CallerContext(
            subject: $"plugin:{plugin.Id}",
            permissions: methods,
            isMainToken: false);
    }

    public ICallerContext ForPrincipal(VerifiedPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return new CallerContext(
            subject: principal.Subject,
            permissions: principal.Permissions,
            isMainToken: principal.IsMainToken);
    }
}
