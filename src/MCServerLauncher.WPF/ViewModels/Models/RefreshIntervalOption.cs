using System.Collections.Generic;
using System.Linq;
using MCServerLauncher.WPF.Modules;

namespace MCServerLauncher.WPF.ViewModels.Models;

public sealed record RefreshIntervalOption(int Seconds, string Display);

public static class RefreshIntervalOptionCatalog
{
    private static readonly int[] AllowedSeconds = [5, 20, 30, 45, 60];

    public static IReadOnlyList<RefreshIntervalOption> All { get; } =
    [
        new(5, Lang.Tr["RefreshInterval_5Seconds"]),
        new(20, Lang.Tr["RefreshInterval_20Seconds"]),
        new(30, Lang.Tr["RefreshInterval_30Seconds"]),
        new(45, Lang.Tr["RefreshInterval_45Seconds"]),
        new(60, Lang.Tr["RefreshInterval_1Minute"])
    ];

    public static int Normalize(int seconds)
    {
        if (AllowedSeconds.Contains(seconds)) return seconds;
        return seconds > AllowedSeconds[^1]
            ? AllowedSeconds[^1]
            : AllowedSeconds.First(value => seconds <= value);
    }
}
