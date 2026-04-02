using System.Runtime.CompilerServices;

namespace MCServerLauncher.ProtocolTests.Helpers;

internal static class ProtocolTestsRuntimeSwitches
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        AppContext.SetSwitch("System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault", true);
    }
}
