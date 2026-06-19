using System;
using System.Text.Json;

namespace MCServerLauncher.Daemon.Serialization;

/// <summary>
/// Controls whether boundary STJ options append reflection fallback resolver.
/// </summary>
public enum DaemonStjReflectionFallbackPolicy
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
    /// Use reflection fallback whenever runtime reflection is enabled.
    /// </summary>
    Enabled
}

internal static class DaemonStjReflectionFallbackPolicyExtensions
{
    private const string ReflectionEnabledSwitch = "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault";

    public static bool ShouldEnableFallback(this DaemonStjReflectionFallbackPolicy policy)
    {
        return policy switch
        {
            DaemonStjReflectionFallbackPolicy.TrimFriendlyDefault => IsReflectionEnabledByDefault(),
            DaemonStjReflectionFallbackPolicy.Disabled => false,
            DaemonStjReflectionFallbackPolicy.Enabled => IsReflectionEnabledByDefault(),
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
