using System;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.WPF.Modules;
using Serilog;

namespace MCServerLauncher.WPF.Services;

internal static class DaemonErrorLocalization
{
    internal static InvalidOperationException ToException(DaemonError error)
    {
        return new InvalidOperationException(GetMessage(error));
    }

    internal static string GetMessage(DaemonError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        Log.Error("[DaemonError] {Code}: {Message}", error.Code, error.Message);

        return error.Code.StartsWith("connection.", StringComparison.Ordinal)
               || error.Code.StartsWith("client.", StringComparison.Ordinal)
            ? Lang.Tr["DaemonConnectionError"]
            : Lang.Tr["Status_Error"];
    }
}
