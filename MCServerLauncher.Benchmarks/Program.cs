using System.Reflection;
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(NormalizeArgs(args));

static string[] NormalizeArgs(string[] args)
{
    var normalized = new List<string>(args.Length);
    var filters = new List<string>();

    for (var i = 0; i < args.Length; i++)
    {
        var current = args[i];

        if (string.Equals(current, "--filter", StringComparison.Ordinal) && i + 1 < args.Length)
        {
            CollectFilters(filters, args[++i]);
            continue;
        }

        if (current.StartsWith("--filter=", StringComparison.Ordinal))
        {
            CollectFilters(filters, current["--filter=".Length..]);
            continue;
        }

        normalized.Add(current);
    }

    if (filters.Count > 0)
    {
        normalized.Add("--filter");
        normalized.AddRange(filters);
    }

    return normalized.ToArray();
}

static void CollectFilters(List<string> filters, string filterValue)
{
    var parts = filterValue.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    if (parts.Length == 0)
    {
        filters.Add(filterValue);
        return;
    }

    filters.AddRange(parts);
}
