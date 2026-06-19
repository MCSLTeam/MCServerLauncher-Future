using System;
using System.Text.Json;

namespace MCServerLauncher.DaemonClient.Serialization;

/// <summary>
/// Controls whether daemonclient RPC STJ options append reflection fallback resolver.
/// </summary>
public enum DaemonClientStjReflectionFallbackPolicy
{
    /// <summary>
    /// Trim-friendly default: only enable reflection fallback when runtime allows it.
    /// </summary>
    TrimFriendlyDefault,

    /// <summary>
    /// Never append reflection fallback resolver.
    /// </summary>
    Disabled,

    /// <summary>
    /// Always append reflection fallback resolver.
    /// </summary>
    Enabled
}

internal static class DaemonClientStjReflectionFallbackPolicyExtensions
{
    private const string ReflectionEnabledSwitch = "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault";

    public static bool ShouldEnableFallback(this DaemonClientStjReflectionFallbackPolicy policy)
    {
        return policy switch
        {
            DaemonClientStjReflectionFallbackPolicy.TrimFriendlyDefault => IsReflectionEnabledByDefault(),
            DaemonClientStjReflectionFallbackPolicy.Disabled => false,
            DaemonClientStjReflectionFallbackPolicy.Enabled => true,
            _ => false
        };
    }

    private static bool IsReflectionEnabledByDefault()
    {
        return AppContext.TryGetSwitch(ReflectionEnabledSwitch, out var enabled)
            ? enabled
            : JsonSerializer.IsReflectionEnabledByDefault;
    }
}
