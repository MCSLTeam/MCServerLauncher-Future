using System.Text.RegularExpressions;
using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Daemon.Management.Communicate;

internal enum InstanceLifecycleSignal
{
    None,
    Ready,
    Crashed,
}

/// <summary>
/// Classifies type-specific process lifecycle signals without owning process state.
/// </summary>
internal interface IInstanceLifecycleObserver
{
    TimeSpan DefaultReadyTimeout { get; }

    InstanceLifecycleSignal ObserveProcessReady();

    InstanceLifecycleSignal ObserveLog(string message, bool isStandardError);
}

internal static class InstanceLifecycleObserverFactory
{
    internal static IInstanceLifecycleObserver Create(InstanceType instanceType) =>
        instanceType.IsMinecraftJavaRuntimeType()
            ? MinecraftInstanceLifecycleObserver.Instance
            : GenericInstanceLifecycleObserver.Instance;
}

internal sealed class GenericInstanceLifecycleObserver : IInstanceLifecycleObserver
{
    internal static readonly GenericInstanceLifecycleObserver Instance = new();

    private GenericInstanceLifecycleObserver()
    {
    }

    public TimeSpan DefaultReadyTimeout => TimeSpan.FromMinutes(2);

    public InstanceLifecycleSignal ObserveProcessReady() => InstanceLifecycleSignal.Ready;

    public InstanceLifecycleSignal ObserveLog(string message, bool isStandardError) =>
        InstanceLifecycleSignal.None;
}

internal sealed class MinecraftInstanceLifecycleObserver : IInstanceLifecycleObserver
{
    private static readonly Regex DonePattern = new(
        @"Done \(\d+\.\d{1,3}s\)! For help, type [""']help[""'](?:\s+or\s+[""']\?[""'])?\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static readonly MinecraftInstanceLifecycleObserver Instance = new();

    private MinecraftInstanceLifecycleObserver()
    {
    }

    public TimeSpan DefaultReadyTimeout => TimeSpan.FromMinutes(2);

    public InstanceLifecycleSignal ObserveProcessReady() => InstanceLifecycleSignal.None;

    public InstanceLifecycleSignal ObserveLog(string message, bool isStandardError)
    {
        if (isStandardError)
            return InstanceLifecycleSignal.None;

        var normalized = message.TrimEnd();
        if (DonePattern.IsMatch(normalized))
            return InstanceLifecycleSignal.Ready;

        return normalized.Contains("Minecraft has crashed", StringComparison.Ordinal)
            ? InstanceLifecycleSignal.Crashed
            : InstanceLifecycleSignal.None;
    }
}
