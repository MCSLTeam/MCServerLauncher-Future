using System.Globalization;
using System.Text.Json;

namespace MCServerLauncher.PerformanceGate;

public static class PerformanceGateCommand
{
    public static int Run(string[] args)
    {
        if (args.Any(static argument => argument is "--help" or "-h"))
        {
            Console.WriteLine("Usage: --baseline <file> --results <directory> [--paired]");
            return 0;
        }

        try
        {
            var options = GateOptions.Parse(args);
            var result = PerformanceGateEvaluator.Evaluate(options.BaselinePath, options.ResultsPath, options.Paired);
            foreach (var message in result.Messages)
                Console.WriteLine(message);

            return result.Passed ? 0 : 1;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Performance gate configuration error: {exception.Message}");
            return 2;
        }
    }

    private sealed record GateOptions(string BaselinePath, string ResultsPath, bool Paired)
    {
        public static GateOptions Parse(string[] args)
        {
            string? baseline = null;
            string? results = null;
            var paired = false;

            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--baseline" when index + 1 < args.Length:
                        baseline = args[++index];
                        break;
                    case "--results" when index + 1 < args.Length:
                        results = args[++index];
                        break;
                    case "--paired":
                        paired = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown or incomplete argument '{args[index]}'.");
                }
            }

            if (string.IsNullOrWhiteSpace(baseline) || string.IsNullOrWhiteSpace(results))
                throw new ArgumentException("Both --baseline and --results are required.");

            return new GateOptions(baseline, results, paired);
        }
    }
}

public sealed record PerformanceGateResult(bool Passed, IReadOnlyList<string> Messages);

public static class PerformanceGateEvaluator
{
    private static readonly string[] EnvironmentKeys =
    [
        "benchmarkdotnet_version",
        "dotnet_cli_version",
        "runtime_version",
        "os_version",
        "architecture",
        "processor",
        "configuration"
    ];

    public static PerformanceGateResult Evaluate(string baselinePath, string resultsPath, bool paired)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselinePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultsPath);

        var baseline = ReadBaseline(baselinePath);
        var report = ReadReport(resultsPath);
        var messages = new List<string>();
        var passed = true;
        var environmentMatches = EnvironmentMatches(baseline.Environment, report.Environment);

        if (!environmentMatches && !paired)
        {
            passed = false;
            messages.Add(
                "FAIL environment fingerprint differs; recapture a paired baseline on the same machine or pass --paired for an explicit same-machine A/B comparison.");
        }
        else
        {
            messages.Add(environmentMatches
                ? "PASS environment fingerprint matches."
                : "WARN environment fingerprint differs; --paired explicitly permits mean comparison.");
        }

        var maximumRegression = baseline.MaximumMeanRegressionPercent / 100.0;
        foreach (var metric in baseline.Metrics)
        {
            if (!report.Metrics.TryGetValue(metric.Benchmark, out var actual))
            {
                passed = false;
                messages.Add($"FAIL {metric.Id}: report does not contain '{metric.Benchmark}'.");
                continue;
            }

            var allocationBaseline = metric.ComparisonAllocatedBytesPerOperation ?? metric.AllocatedBytesPerOperation;
            if (allocationBaseline is null || actual.AllocatedBytesPerOperation is null)
            {
                passed = false;
                messages.Add($"FAIL {metric.Id}: allocation data is required in both baseline and report.");
            }
            else
            {
                var allocationLimit = allocationBaseline.Value * (1 + maximumRegression);
                var allocationPasses = metric.RequireZeroAllocation
                    ? actual.AllocatedBytesPerOperation.Value <= 0
                    : actual.AllocatedBytesPerOperation.Value <= allocationLimit;
                if (!allocationPasses)
                {
                    passed = false;
                    messages.Add(
                        $"FAIL {metric.Id}: {actual.AllocatedBytesPerOperation.Value.ToString("F2", CultureInfo.InvariantCulture)} B/op > {allocationLimit.ToString("F2", CultureInfo.InvariantCulture)} B/op.");
                }
                else
                {
                    messages.Add(
                        $"PASS {metric.Id}: allocation {actual.AllocatedBytesPerOperation.Value.ToString("F2", CultureInfo.InvariantCulture)} B/op.");
                }
            }

            var meanBaseline = metric.ComparisonMeanNanoseconds ?? metric.MeanNanoseconds;
            if (meanBaseline is null)
                continue;
            if (!environmentMatches && !paired)
                continue;
            if (actual.MeanNanoseconds is null)
            {
                passed = false;
                messages.Add($"FAIL {metric.Id}: mean data is required for the paired comparison.");
                continue;
            }

            var meanLimit = meanBaseline.Value * (1 + maximumRegression);
            if (actual.MeanNanoseconds.Value > meanLimit)
            {
                passed = false;
                messages.Add(
                    $"FAIL {metric.Id}: {actual.MeanNanoseconds.Value.ToString("F2", CultureInfo.InvariantCulture)} ns/op > {meanLimit.ToString("F2", CultureInfo.InvariantCulture)} ns/op.");
            }
            else
            {
                messages.Add(
                    $"PASS {metric.Id}: mean {actual.MeanNanoseconds.Value.ToString("F2", CultureInfo.InvariantCulture)} ns/op.");
            }
        }

        messages.Add(passed ? "Performance gate passed." : "Performance gate failed.");
        return new PerformanceGateResult(passed, messages);
    }

    private static BaselineDocument ReadBaseline(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The performance baseline file was not found.", path);

        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        var environment = ReadEnvironment(root.GetProperty("environment"));
        var gate = root.GetProperty("gate");
        var maximumRegression = gate.TryGetProperty("maximum_mean_regression_percent", out var value)
            ? value.GetDouble()
            : 25.0;
        if (!double.IsFinite(maximumRegression) || maximumRegression < 0)
            throw new InvalidDataException("The performance baseline regression threshold must be a finite non-negative number.");
        var metrics = root.GetProperty("metrics")
            .EnumerateArray()
            .Select(ReadMetric)
            .ToArray();
        if (metrics.Length == 0)
            throw new InvalidDataException("The performance baseline contains no metrics.");

        return new BaselineDocument(environment, maximumRegression, metrics);
    }

    private static BaselineMetric ReadMetric(JsonElement element)
    {
        var mean = element.TryGetProperty("mean_nanoseconds", out var meanElement) &&
                   meanElement.ValueKind != JsonValueKind.Null
            ? meanElement.GetDouble()
            : (double?)null;
        var allocation = element.TryGetProperty("allocated_bytes_per_operation", out var allocationElement) &&
                         allocationElement.ValueKind != JsonValueKind.Null
            ? allocationElement.GetDouble()
            : (double?)null;
        var comparison = element.TryGetProperty("comparison", out var comparisonElement) &&
                         comparisonElement.ValueKind == JsonValueKind.Object
            ? comparisonElement
            : (JsonElement?)null;
        var comparisonMean = comparison is { } comparisonValue &&
                             comparisonValue.TryGetProperty("mean_nanoseconds", out var comparisonMeanElement) &&
                             comparisonMeanElement.ValueKind != JsonValueKind.Null
            ? comparisonMeanElement.GetDouble()
            : (double?)null;
        var comparisonAllocation = comparison is { } comparisonObject &&
                                   comparisonObject.TryGetProperty("allocated_bytes_per_operation", out var comparisonAllocationElement) &&
                                   comparisonAllocationElement.ValueKind != JsonValueKind.Null
            ? comparisonAllocationElement.GetDouble()
            : (double?)null;
        return new BaselineMetric(
            element.GetProperty("id").GetString() ?? throw new InvalidDataException("A baseline metric id is missing."),
            element.GetProperty("benchmark").GetString() ?? throw new InvalidDataException("A baseline benchmark name is missing."),
            mean,
            allocation,
            comparisonMean,
            comparisonAllocation,
            element.TryGetProperty("require_zero_allocation", out var zero) && zero.GetBoolean());
    }

    private static BenchmarkReport ReadReport(string path)
    {
        var files = File.Exists(path)
            ? [path]
            : Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*report-full.json", SearchOption.AllDirectories).ToArray()
                : throw new DirectoryNotFoundException($"Benchmark results path '{path}' was not found.");
        if (files.Length == 0)
            throw new InvalidDataException($"No report-full.json files were found under '{path}'.");

        Dictionary<string, string>? environment = null;
        var metrics = new Dictionary<string, BenchmarkMetric>(StringComparer.Ordinal);
        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(file));
            var root = document.RootElement;
            var reportEnvironment = ReadReportEnvironment(root.GetProperty("HostEnvironmentInfo"));
            if (environment is null)
                environment = reportEnvironment;
            else if (!EnvironmentMatches(environment, reportEnvironment))
                throw new InvalidDataException("Benchmark result files use different environment fingerprints.");
            foreach (var benchmark in root.GetProperty("Benchmarks").EnumerateArray())
            {
                var statistics = benchmark.GetProperty("Statistics");
                if (statistics.ValueKind == JsonValueKind.Null)
                    continue;

                var memory = benchmark.GetProperty("Memory");
                var allocationElement = memory.GetProperty("BytesAllocatedPerOperation");
                var allocation = allocationElement.ValueKind == JsonValueKind.Null
                    ? (double?)null
                    : allocationElement.GetDouble();
                var name = benchmark.GetProperty("FullName").GetString()
                           ?? throw new InvalidDataException("A benchmark report entry is missing FullName.");
                metrics[name] = new BenchmarkMetric(
                    statistics.GetProperty("Mean").GetDouble(),
                    allocation);
            }
        }

        return new BenchmarkReport(
            environment ?? throw new InvalidDataException("Benchmark result files did not contain an environment fingerprint."),
            metrics);
    }

    private static Dictionary<string, string> ReadEnvironment(JsonElement element) =>
        new(EnvironmentKeys.ToDictionary(key => key, key => GetString(element, key), StringComparer.Ordinal));

    private static Dictionary<string, string> ReadReportEnvironment(JsonElement element) => new(StringComparer.Ordinal)
    {
        ["benchmarkdotnet_version"] = GetString(element, "BenchmarkDotNetVersion"),
        ["dotnet_cli_version"] = GetString(element, "DotNetCliVersion"),
        ["runtime_version"] = GetString(element, "RuntimeVersion"),
        ["os_version"] = GetString(element, "OsVersion"),
        ["architecture"] = GetString(element, "Architecture"),
        ["processor"] = GetString(element, "ProcessorName"),
        ["configuration"] = GetString(element, "Configuration")
    };

    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
            ? value.ToString()
            : throw new InvalidDataException($"Benchmark environment is missing '{propertyName}'.");

    private static bool EnvironmentMatches(IReadOnlyDictionary<string, string> baseline, IReadOnlyDictionary<string, string> report) =>
        EnvironmentKeys.All(key => string.Equals(baseline[key], report[key], StringComparison.Ordinal));

    private sealed record BaselineDocument(
        IReadOnlyDictionary<string, string> Environment,
        double MaximumMeanRegressionPercent,
        IReadOnlyList<BaselineMetric> Metrics);

    private sealed record BaselineMetric(
        string Id,
        string Benchmark,
        double? MeanNanoseconds,
        double? AllocatedBytesPerOperation,
        double? ComparisonMeanNanoseconds,
        double? ComparisonAllocatedBytesPerOperation,
        bool RequireZeroAllocation);

    private sealed record BenchmarkReport(
        IReadOnlyDictionary<string, string> Environment,
        IReadOnlyDictionary<string, BenchmarkMetric> Metrics);

    private sealed record BenchmarkMetric(double? MeanNanoseconds, double? AllocatedBytesPerOperation);
}
