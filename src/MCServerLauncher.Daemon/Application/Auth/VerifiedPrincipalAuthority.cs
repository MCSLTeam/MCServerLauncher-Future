using System.Runtime.CompilerServices;
using MCServerLauncher.Daemon.API.Plugins;

namespace MCServerLauncher.Daemon.ApplicationCore.Auth;

internal sealed class VerifiedPrincipalAuthority
{
    private readonly ConditionalWeakTable<VerifiedPrincipal, Registration> _verified = new();

    internal VerifiedPrincipal Register(
        PluginIdentity plugin,
        string audience,
        VerifiedPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentNullException.ThrowIfNull(principal);
        if (!string.Equals(principal.Audience, audience, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The verified principal audience does not match its authentication request.",
                nameof(principal));
        }

        _verified.Add(
            principal,
            new Registration(plugin.Id, plugin.Version, audience));
        return principal;
    }

    internal VerifiedPrincipal Register(PluginIdentity plugin, VerifiedPrincipal principal) =>
        Register(plugin, principal.Audience, principal);

    internal void EnsureRegistered(PluginIdentity plugin, VerifiedPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(principal);
        if (!_verified.TryGetValue(principal, out var registration))
        {
            throw new ArgumentException(
                "The principal was not produced by this plugin host's authentication service.",
                nameof(principal));
        }

        if (!string.Equals(registration.PluginId, plugin.Id, StringComparison.Ordinal) ||
            !string.Equals(registration.PluginVersion, plugin.Version, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The principal was authenticated for a different plugin.",
                nameof(principal));
        }

        if (!string.Equals(registration.Audience, principal.Audience, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The principal audience no longer matches its verified provenance.",
                nameof(principal));
        }
    }

    private sealed record Registration(
        string PluginId,
        string PluginVersion,
        string Audience);
}
