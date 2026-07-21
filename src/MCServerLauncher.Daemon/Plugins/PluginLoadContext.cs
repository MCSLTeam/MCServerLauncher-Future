using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustyOptions;

namespace MCServerLauncher.Daemon.Plugins;

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    internal PluginLoadContext(string entryAssemblyPath, string pluginId)
        : base($"MCServerLauncher.Plugin.{pluginId}", isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The daemon plugin product is an untrimmed JIT host; sidecar plugin assemblies are loaded intentionally at startup.")]
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);
        if (PluginAssemblyPolicy.IsShared(assemblyName.Name))
            return ResolveSharedAssembly(assemblyName.Name!);

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The daemon plugin product is an untrimmed JIT host; sidecar plugin assemblies are loaded intentionally at startup.")]
    internal Assembly LoadEntryAssembly(string entryAssemblyPath) => LoadFromAssemblyPath(entryAssemblyPath);

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(libraryPath);
    }

    private static Assembly ResolveSharedAssembly(string name) => name switch
    {
        "MCServerLauncher.Daemon.API" => typeof(IDaemonPlugin).Assembly,
        // Resolve the actual shared Common contract assembly. RustyOptions.Unit is
        // intentionally a different assembly and must never satisfy this binding.
        "MCServerLauncher.Common" => typeof(JsonRpcRequestEnvelope).Assembly,
        "RustyOptions" => typeof(Result<,>).Assembly,
        "Microsoft.Extensions.Logging.Abstractions" => typeof(ILogger).Assembly,
        // Shared DI abstractions preserve IServiceCollection type identity across ALCs.
        // DI implementation assemblies remain private to the plugin bundle.
        "Microsoft.Extensions.DependencyInjection.Abstractions" => typeof(IServiceCollection).Assembly,
        _ => throw new InvalidOperationException($"Unsupported shared plugin assembly '{name}'.")
    };
}
