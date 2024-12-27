namespace MCServerLauncher.Daemon.Utils;

public static class StringExtensions
{
    public static string? PascalCaseToSnakeCase(this string? str)
    {
        if (string.IsNullOrEmpty(str)) return str;

        return string.Concat(str.Select((x, i) =>
            i > 0 && char.IsUpper(x) ? "_" + x.ToString().ToLower() : x.ToString().ToLower()));
    }
}