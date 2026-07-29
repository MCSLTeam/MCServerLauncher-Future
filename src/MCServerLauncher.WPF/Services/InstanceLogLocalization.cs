using System;
using System.Collections.Generic;
using System.Linq;
using MCServerLauncher.WPF.Modules;

namespace MCServerLauncher.WPF.Services;

internal static class InstanceLogLocalization
{
    private const string LifecyclePrefix = "[MCSL] Instance ";

    public static bool IsLifecycleMessage(string message) =>
        message.StartsWith(LifecyclePrefix, StringComparison.Ordinal);

    public static bool TryLocalize(string message, out string localizedMessage)
    {
        switch (message.TrimEnd('\r', '\n'))
        {
            case LifecyclePrefix + "starting.":
                localizedMessage = Lang.Tr["InstanceLog_Starting"];
                return true;
            case LifecyclePrefix + "stopped.":
                localizedMessage = Lang.Tr["InstanceLog_Stopped"];
                return true;
            case LifecyclePrefix + "running.":
            case LifecyclePrefix + "stopping.":
            case LifecyclePrefix + "crashed.":
                localizedMessage = string.Empty;
                return false;
            default:
                localizedMessage = message;
                return !string.IsNullOrWhiteSpace(message);
        }
    }

    public static string[] LocalizeHistory(IEnumerable<string> messages) =>
        messages
            .Select(message => TryLocalize(message, out var localizedMessage) ? localizedMessage : null)
            .Where(message => message is not null)
            .Select(message => message!)
            .ToArray();
}