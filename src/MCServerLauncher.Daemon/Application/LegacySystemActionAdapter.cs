using MCServerLauncher.Daemon.Utils.LazyCell;
using LegacyJavaRuntime = MCServerLauncher.Common.ProtoType.JavaInfo;
using LegacySystemInfo = MCServerLauncher.Common.ProtoType.Status.SystemInfo;

namespace MCServerLauncher.Daemon.ApplicationCore;

/// <summary>
/// Preserves the V1 action executor's exception and cancellation translation while the
/// transport-neutral system application returns safe typed errors.
/// </summary>
internal sealed class LegacySystemActionAdapter(
    IAsyncTimedLazyCell<LegacySystemInfo> systemInfoCell,
    IAsyncTimedLazyCell<LegacyJavaRuntime[]> javaRuntimeCell)
{
    internal async Task<LegacySystemInfo> GetSystemInfoAsync()
    {
        return await systemInfoCell.Value;
    }

    internal async Task<LegacyJavaRuntime[]> ListJavaRuntimesAsync()
    {
        return await javaRuntimeCell.Value;
    }
}
