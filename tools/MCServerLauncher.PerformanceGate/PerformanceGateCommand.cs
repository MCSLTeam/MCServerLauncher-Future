using System.Globalization;
using System.Text.Json;

namespace MCServerLauncher.PerformanceGate;

public static class PerformanceGateCommand
{
    public static int Run(string[] args)
    {
        if (args.Any(static argument => argument is "--help" or "-h"))
        {
            Console.WriteLine("Usage: --baseline <file> --results <directory> [--reference-results <directory> --paired]");
            return 0;
        }

        try
        {
            var options = GateOptions.Parse(args);
            var result = PerformanceGateEvaluator.Evaluate(
                options.BaselinePath,
                options.ResultsPath,
                options.Paired,
                options.ReferenceResultsPath);
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

    private sealed record GateOptions(
        string BaselinePath,
        string ResultsPath,
        bool Paired,
        string? ReferenceResultsPath)
    {
        public static GateOptions Parse(string[] args)
        {
            string? baseline = null;
            string? results = null;
            string? referenceResults = null;
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
                    case "--reference-results" when index + 1 < args.Length:
                        referenceResults = args[++index];
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
            if (referenceResults is not null && !paired)
                throw new ArgumentException("--reference-results requires --paired so the reference and candidate are compared on the same runner.");
            if (paired && referenceResults is null)
                throw new ArgumentException("--paired requires --reference-results from the immutable V1 reference checkout.");

            return new GateOptions(baseline, results, paired, referenceResults);
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

    public static PerformanceGateResult Evaluate(
        string baselinePath,
        string resultsPath,
        bool paired,
        string? referenceResultsPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselinePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultsPath);
        if (paired && string.IsNullOrWhiteSpace(referenceResultsPath))
            throw new ArgumentException("Paired comparison requires an immutable V1 reference report.", nameof(referenceResultsPath));
        if (!paired && referenceResultsPath is not null)
            throw new ArgumentException("Reference results are only valid for a paired comparison.", nameof(referenceResultsPath));

        var baseline = ReadBaseline(baselinePath);
        var report = ReadReport(resultsPath);
        var referenceReport = referenceResultsPath is null ? null : ReadReport(referenceResultsPath);
        var messages = new List<string>();
        var passed = true;
        var environmentMatches = EnvironmentMatches(baseline.Environment, report.Environment);
        var referenceEnvironmentMatches = referenceReport is null ||
                                         EnvironmentMatches(referenceReport.Environment, report.Environment);

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

        if (referenceReport is not null)
        {
            if (!referenceEnvironmentMatches)
            {
                passed = false;
                messages.Add("FAIL paired reference and candidate benchmark environments differ.");
            }
            else
            {
                messages.Add("PASS paired reference and candidate benchmark environments match.");
            }
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

            var referenceMetric = referenceReport is null || metric.ComparisonBenchmark is null
                ? null
                : referenceReport.Metrics.TryGetValue(metric.ComparisonBenchmark, out var pairedMetric)
                    ? pairedMetric
                    : throw new InvalidDataException(
                        $"The paired reference report does not contain '{metric.ComparisonBenchmark}'.");
            var staticAllocationBaseline = metric.ComparisonAllocatedBytesPerOperation ??
                                           metric.AllocatedBytesPerOperation;
            var allocationBaseline = Minimum(
                staticAllocationBaseline,
                referenceMetric?.AllocatedBytesPerOperation);
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

            var staticMeanBaseline = metric.ComparisonMeanNanoseconds ?? metric.MeanNanoseconds;
            var meanBaseline = referenceMetric?.MeanNanoseconds ?? staticMeanBaseline;
            if (meanBaseline is null)
                continue;
            if ((referenceMetric is null && !environmentMatches) ||
                (referenceMetric is not null && !referenceEnvironmentMatches))
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

    private static double? Minimum(double? first, double? second) => (first, second) switch
    {
        ({ } left, { } right) => Math.Min(left, right),
        ({ } left, null) => left,
        (null, { } right) => right,
        _ => null
    };

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
        var mean = ReadOptionalNonNegativeDouble(element, "mean_nanoseconds");
        var allocation = ReadOptionalNonNegativeDouble(element, "allocated_bytes_per_operation");
        var comparison = element.TryGetProperty("comparison", out var comparisonElement) &&
                         comparisonElement.ValueKind == JsonValueKind.Object
            ? comparisonElement
            : (JsonElement?)null;
        var comparisonMean = comparison is { } comparisonValue
            ? ReadOptionalNonNegativeDouble(comparisonValue, "mean_nanoseconds")
            : null;
        var comparisonAllocation = comparison is { } comparisonObject
            ? ReadOptionalNonNegativeDouble(comparisonObject, "allocated_bytes_per_operation")
            : null;
        var comparisonBenchmark = comparison is { } comparisonBenchmarkObject &&
                                  comparisonBenchmarkObject.TryGetProperty("benchmark", out var comparisonBenchmarkElement)
            ? comparisonBenchmarkElement.GetString()
            : null;
        return new BaselineMetric(
            element.GetProperty("id").GetString() ?? throw new InvalidDataException("A baseline metric id is missing."),
            element.GetProperty("benchmark").GetString() ?? throw new InvalidDataException("A baseline benchmark name is missing."),
            mean,
            allocation,
            comparisonBenchmark,
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
                var allocation = ReadOptionalNonNegativeDouble(memory, "BytesAllocatedPerOperation");
                var name = benchmark.GetProperty("FullName").GetString()
                           ?? throw new InvalidDataException("A benchmark report entry is missing FullName.");
                var metric = new BenchmarkMetric(
                    ReadRequiredNonNegativeDouble(statistics, "Mean"),
                    allocation);
                if (!metrics.TryAdd(name, metric))
                    throw new InvalidDataException($"Benchmark report contains duplicate FullName '{name}'.");
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

    private static double? ReadOptionalNonNegativeDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;

        return ReadNonNegativeDouble(value, propertyName);
    }

    private static double ReadRequiredNonNegativeDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
            throw new InvalidDataException($"Benchmark metric is missing '{propertyName}'.");

        return ReadNonNegativeDouble(value, propertyName);
    }

    private static double ReadNonNegativeDouble(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"Benchmark metric '{propertyName}' must be a finite non-negative JSON number.");

        try
        {
            var value = element.GetDouble();
            if (!double.IsFinite(value) || value < 0)
                throw new InvalidDataException($"Benchmark metric '{propertyName}' must be finite and non-negative.");

            return value;
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"Benchmark metric '{propertyName}' must be a finite non-negative JSON number.", exception);
        }
    }

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
        string? ComparisonBenchmark,
        double? ComparisonMeanNanoseconds,
        double? ComparisonAllocatedBytesPerOperation,
        bool RequireZeroAllocation);

    private sealed record BenchmarkReport(
        IReadOnlyDictionary<string, string> Environment,
        IReadOnlyDictionary<string, BenchmarkMetric> Metrics);

    private sealed record BenchmarkMetric(double? MeanNanoseconds, double? AllocatedBytesPerOperation);
}
