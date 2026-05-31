using System.Text.RegularExpressions;

namespace MCServerLauncher.Utils.Minecraft;

/// <summary>
///     Minecraft 版本排序工具：把发行版/快照/特殊代号统一映射成可比较的元组后降序排列。
/// </summary>
public static class McVersionSequencer
{
    private static readonly Func<string, (int, int, int, int)> VersionToTuple = version =>
    {
        // special versions
        if (!version.Contains(".") && !version.Contains("-"))
            return version switch
            {
                "horn" => (1, 19, 2, 0),
                "GreatHorn" => (1, 19, 3, 0),
                "Executions" => (1, 19, 4, 0),
                "Trials" => (1, 20, 1, 0),
                "Net" => (1, 20, 2, 0),
                "Whisper" => (1, 20, 4, 0),
                "general" => (0, 0, 0, 0),
                "snapshot" => (0, 0, 0, 0),
                "release" => (0, 0, 0, 0),
                _ => (0, 0, 0, 0)
            };

        // snapshot versions
        var snapshotMatch = Regex.Match(version, @"^(\d+)w(\d+)([a-z])$");
        if (snapshotMatch.Success)
        {
            int year = int.Parse(snapshotMatch.Groups[1].Value);
            int week = int.Parse(snapshotMatch.Groups[2].Value);
            int revision = snapshotMatch.Groups[3].Value[0] - 'a' + 1;
            return (year, week, revision, 0);
        }

        // other versions
        version = Regex.Replace(version.ToLower(), @"[-_]", ".")
            .Replace("rc", "")
            .Replace(" Pre-Release ", ".pre")
            .Replace("pre", "")
            .Replace("snapshot", "0")
            .Replace(".beta", "beta")
            .Replace("beta", "0");

        // Remove non-numeric parts for parsing
        var parts = version.Split('.').Select(p => new string(p.Where(char.IsDigit).ToArray())).Where(p => !string.IsNullOrEmpty(p)).ToArray();

        if (parts.Length == 0) return (0, 0, 0, 0);
        if (parts.Length == 1) return (int.Parse(parts[0]), 0, 0, 0);
        if (parts.Length == 2) return (int.Parse(parts[0]), int.Parse(parts[1]), 0, 0);
        if (parts.Length == 3) return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), 0);
        return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
    };

    private static readonly Func<string, string> VersionComparator = version =>
    {
        var versionTuple = VersionToTuple(version);
        return $"{versionTuple.Item1:D3}.{versionTuple.Item2:D3}.{versionTuple.Item3:D3}";
    };

    public static List<string?> Sequence(List<string>? originalList)
    {
        return (originalList ?? throw new ArgumentNullException(nameof(originalList))).OrderByDescending(VersionComparator!).ToList()!;
    }
}
