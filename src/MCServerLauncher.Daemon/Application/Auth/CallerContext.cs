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
    private readonly DateTimeOffset _expiresAt;
    private readonly TimeProvider _timeProvider;

    public CallerContext(
        string subject,
        ImmutableArray<string> permissions,
        bool isMainToken,
        DateTimeOffset? expiresAt = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        Subject = subject;
        Permissions = permissions.IsDefault
            ? ImmutableArray<string>.Empty
            : permissions;
        IsMainToken = isMainToken;
        _expiresAt = expiresAt ?? DateTimeOffset.MaxValue;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _compiled = new Permissions(Permissions.ToArray());
    }

    public string Subject { get; }

    public ImmutableArray<string> Permissions { get; }

    public bool IsMainToken { get; }

    public bool HasPermission(string methodPermission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodPermission);
        if (IsExpired())
            return false;

        return HasPermissionCore(methodPermission);
    }

    public Result<Unit, DaemonError> EnsurePermission(string methodPermission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodPermission);
        if (IsExpired())
        {
            return Result.Err<Unit, DaemonError>(
                new PermissionDaemonError(
                    "auth.principal_expired",
                    "The verified principal has expired."));
        }

        if (HasPermissionCore(methodPermission))
            return Result.Ok<Unit, DaemonError>(Unit.Default);

        return Result.Err<Unit, DaemonError>(
            new PermissionDaemonError(
                "auth.permission_denied",
                $"The caller is not permitted to invoke '{methodPermission}'."));
    }

    private bool HasPermissionCore(string methodPermission)
    {
        if (IsMainToken || Permissions.Any(static p => p == "*"))
            return true;

        var required = Permission.Of(methodPermission);
        return _compiled.Matches(required);
    }

    private bool IsExpired() => _expiresAt <= _timeProvider.GetUtcNow();
}

internal sealed class CallerContextFactory : ICallerContextFactory
{
    private readonly VerifiedPrincipalAuthority _verifiedPrincipals;
    private readonly TimeProvider _timeProvider;
    private readonly PluginIdentity? _pluginScope;

    internal CallerContextFactory()
        : this(new VerifiedPrincipalAuthority(), TimeProvider.System, pluginScope: null)
    {
    }

    internal CallerContextFactory(
        VerifiedPrincipalAuthority verifiedPrincipals,
        TimeProvider? timeProvider = null)
        : this(verifiedPrincipals, timeProvider ?? TimeProvider.System, pluginScope: null)
    {
    }

    private CallerContextFactory(
        VerifiedPrincipalAuthority verifiedPrincipals,
        TimeProvider timeProvider,
        PluginIdentity? pluginScope)
    {
        _verifiedPrincipals = verifiedPrincipals ?? throw new ArgumentNullException(nameof(verifiedPrincipals));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _pluginScope = pluginScope;
    }

    internal VerifiedPrincipalAuthority VerifiedPrincipals => _verifiedPrincipals;

    internal CallerContextFactory ForPlugin(PluginIdentity plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        if (_pluginScope is not null)
        {
            EnsurePluginScope(plugin);
            return this;
        }

        return new CallerContextFactory(_verifiedPrincipals, _timeProvider, plugin);
    }

    public ICallerContext CreateHost(
        PluginIdentity plugin,
        IEnumerable<string> grantedFeatureIds)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(grantedFeatureIds);
        EnsurePluginScope(plugin);
        var methods = FeatureCatalog.ExpandMethodPermissions(grantedFeatureIds);
        return new CallerContext(
            subject: $"{PrincipalIdentityPolicy.PluginHostSubjectPrefix}{plugin.Id}",
            permissions: methods,
            isMainToken: false);
    }

    public ICallerContext ForPrincipal(VerifiedPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var plugin = _pluginScope ?? throw new InvalidOperationException(
            "A plugin scope is required before binding a verified principal.");
        _verifiedPrincipals.EnsureRegistered(plugin, principal);
        if (principal.IsMainToken)
            PrincipalIdentityPolicy.ValidateMainTokenSubject(principal.Subject, nameof(principal));
        else
            PrincipalIdentityPolicy.ValidateExternalSubject(principal.Subject, nameof(principal));

        return new CallerContext(
            subject: principal.Subject,
            permissions: principal.Permissions,
            isMainToken: principal.IsMainToken,
            expiresAt: principal.ExpiresAt,
            timeProvider: _timeProvider);
    }

    private void EnsurePluginScope(PluginIdentity plugin)
    {
        if (_pluginScope is null)
            return;
        if (string.Equals(_pluginScope.Id, plugin.Id, StringComparison.Ordinal) &&
            string.Equals(_pluginScope.Version, plugin.Version, StringComparison.Ordinal))
        {
            return;
        }

        throw new ArgumentException(
            "The caller-context factory is bound to a different plugin.",
            nameof(plugin));
    }
}
