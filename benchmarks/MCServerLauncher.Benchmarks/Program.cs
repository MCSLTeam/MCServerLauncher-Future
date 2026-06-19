using System.Reflection;
using BenchmarkDotNet.Running;

foreach (var runArgs in NormalizeArgs(args))
{
    BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(runArgs);
}

static IReadOnlyList<string[]> NormalizeArgs(string[] args)
{
    var normalized = new List<string>(args.Length);
    var filters = new List<string>();

    for (var i = 0; i < args.Length; i++)
    {
        var current = args[i];

        if (string.Equals(current, "--filter", StringComparison.Ordinal) && i + 1 < args.Length)
        {
            CollectFilters(filters, args[++i]);

            while (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                CollectFilters(filters, args[++i]);
            }

            continue;
        }

        if (current.StartsWith("--filter=", StringComparison.Ordinal))
        {
            CollectFilters(filters, current["--filter=".Length..]);
            continue;
        }

        normalized.Add(current);
    }

    if (filters.Count == 0)
    {
        return [normalized.ToArray()];
    }

    var runs = new List<string[]>(filters.Count);

    foreach (var filter in filters)
    {
        var run = new List<string>(normalized.Count + 2);
        run.AddRange(normalized);
        run.Add("--filter");
        run.Add(filter);
        runs.Add(run.ToArray());
    }

    return runs;
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
