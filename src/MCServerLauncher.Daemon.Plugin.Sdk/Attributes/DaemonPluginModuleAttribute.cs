namespace MCServerLauncher.Daemon.Plugin.Sdk;

/// <summary>
/// Marks a partial plugin module class for SDK source generation.
/// The generator emits a <c>DaemonPluginAdapter</c> implementing
/// <see cref="MCServerLauncher.Daemon.API.Plugins.IDaemonPlugin"/>, plus
/// feature-gated context types derived from the project's <c>mcsl-plugin.json</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DaemonPluginModuleAttribute : Attribute
{
}
