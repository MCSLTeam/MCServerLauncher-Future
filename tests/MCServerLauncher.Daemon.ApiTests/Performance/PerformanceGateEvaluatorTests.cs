using MCServerLauncher.PerformanceGate;

namespace MCServerLauncher.Daemon.ApiTests.Performance;

public sealed class PerformanceGateEvaluatorTests
{
    [Fact]
    public void MatchingEnvironmentEnforcesZeroAllocationAndMeanThreshold()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "baseline.json"), BaselineJson("1.0", 0));
        File.WriteAllText(Path.Combine(directory.Path, "report-full.json"), ReportJson("1.0", 0));

        var result = PerformanceGateEvaluator.Evaluate(
            Path.Combine(directory.Path, "baseline.json"),
            directory.Path,
            paired: false);

        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Messages));
    }

    [Fact]
    public void MismatchedEnvironmentRequiresExplicitPairedComparison()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "baseline.json"), BaselineJson("1.0", 0));
        File.WriteAllText(Path.Combine(directory.Path, "report-full.json"), ReportJson("2.0", 0));

        var result = PerformanceGateEvaluator.Evaluate(
            Path.Combine(directory.Path, "baseline.json"),
            directory.Path,
            paired: false);

        Assert.False(result.Passed);
        Assert.Contains(result.Messages, message => message.StartsWith("FAIL environment fingerprint", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitComparisonThresholdCanGateAV2MetricAgainstAnOlderBaseline()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "baseline.json"), BaselineWithComparisonJson());
        File.WriteAllText(Path.Combine(directory.Path, "report-full.json"), ReportJson("1.0", 0, 2.0));

        var result = PerformanceGateEvaluator.Evaluate(
            Path.Combine(directory.Path, "baseline.json"),
            directory.Path,
            paired: false);

        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Messages));
    }

    [Fact]
    public void SameRunnerReferenceReportDrivesPairedMeanAndAllocationComparison()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "baseline.json"), MappedComparisonBaselineJson());
        File.WriteAllText(Path.Combine(directory.Path, "reference-report-full.json"), ReportJson("runner", 100, 10.0, "Test.V1.Benchmark"));
        File.WriteAllText(Path.Combine(directory.Path, "candidate-report-full.json"), ReportJson("runner", 11, 11.0, "Test.V2.Benchmark"));

        var result = PerformanceGateEvaluator.Evaluate(
            Path.Combine(directory.Path, "baseline.json"),
            Path.Combine(directory.Path, "candidate-report-full.json"),
            paired: true,
            referenceResultsPath: Path.Combine(directory.Path, "reference-report-full.json"));

        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Messages));
        Assert.Contains(result.Messages, message => message.StartsWith("PASS paired reference", StringComparison.Ordinal));
    }

    [Fact]
    public void PairedComparisonUsesTheMappedV1BenchmarkName()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "baseline.json"), MappedComparisonBaselineJson());
        File.WriteAllText(Path.Combine(directory.Path, "reference-report-full.json"), ReportJson("runner", 100, 8.0, "Test.V1.Benchmark"));
        File.WriteAllText(Path.Combine(directory.Path, "candidate-report-full.json"), ReportJson("runner", 11, 11.0, "Test.V2.Benchmark"));

        var result = PerformanceGateEvaluator.Evaluate(
            Path.Combine(directory.Path, "baseline.json"),
            Path.Combine(directory.Path, "candidate-report-full.json"),
            paired: true,
            referenceResultsPath: Path.Combine(directory.Path, "reference-report-full.json"));

        Assert.False(result.Passed);
        Assert.Contains(result.Messages, message => message.StartsWith("FAIL request.dispatch.ping.v2: 11.00 ns/op > 10.00 ns/op.", StringComparison.Ordinal));
    }

    [Fact]
    public void PairedReferenceCannotRelaxTheStaticV1AllocationThreshold()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "baseline.json"), MappedComparisonBaselineJson());
        File.WriteAllText(Path.Combine(directory.Path, "reference-report-full.json"), ReportJson("runner", 1000, 8.0, "Test.V1.Benchmark"));
        File.WriteAllText(Path.Combine(directory.Path, "candidate-report-full.json"), ReportJson("runner", 20, 9.0, "Test.V2.Benchmark"));

        var result = PerformanceGateEvaluator.Evaluate(
            Path.Combine(directory.Path, "baseline.json"),
            Path.Combine(directory.Path, "candidate-report-full.json"),
            paired: true,
            referenceResultsPath: Path.Combine(directory.Path, "reference-report-full.json"));

        Assert.False(result.Passed);
        Assert.Contains(result.Messages, message => message.StartsWith("FAIL request.dispatch.ping.v2: 20.00 B/op > 12.50 B/op.", StringComparison.Ordinal));
    }

    [Fact]
    public void PairedComparisonFailsWhenTheMappedV1ReferenceIsMissing()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "baseline.json"), MappedComparisonBaselineJson());
        File.WriteAllText(Path.Combine(directory.Path, "reference-report-full.json"), ReportJson("runner", 100, 8.0, "Test.Other.Benchmark"));
        File.WriteAllText(Path.Combine(directory.Path, "candidate-report-full.json"), ReportJson("runner", 110, 9.0, "Test.V2.Benchmark"));

        var exception = Assert.Throws<InvalidDataException>(() => PerformanceGateEvaluator.Evaluate(
            Path.Combine(directory.Path, "baseline.json"),
            Path.Combine(directory.Path, "candidate-report-full.json"),
            paired: true,
            referenceResultsPath: Path.Combine(directory.Path, "reference-report-full.json")));

        Assert.Contains("Test.V1.Benchmark", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PairedComparisonRequiresAnExplicitReferenceReport()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "baseline.json"), BaselineJson("runner", 0));
        File.WriteAllText(Path.Combine(directory.Path, "report-full.json"), ReportJson("runner", 0));

        Assert.Throws<ArgumentException>(() => PerformanceGateEvaluator.Evaluate(
            Path.Combine(directory.Path, "baseline.json"),
            directory.Path,
            paired: true));
    }

    [Fact]
    public void V2OnlyMetricsDoNotRequireAV1ReferenceEntry()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "baseline.json"), BaselineJson("runner", 0));
        File.WriteAllText(Path.Combine(directory.Path, "reference-report-full.json"), ReportJson("runner", 1, 1.0, "Test.V1.Only"));
        File.WriteAllText(Path.Combine(directory.Path, "candidate-report-full.json"), ReportJson("runner", 0));

        var result = PerformanceGateEvaluator.Evaluate(
            Path.Combine(directory.Path, "baseline.json"),
            Path.Combine(directory.Path, "candidate-report-full.json"),
            paired: true,
            referenceResultsPath: Path.Combine(directory.Path, "reference-report-full.json"));

        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Messages));
    }

    [Fact]
    public void PairedComparisonFailsWhenReferenceAndCandidateEnvironmentsDiffer()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "baseline.json"), MappedComparisonBaselineJson());
        File.WriteAllText(Path.Combine(directory.Path, "reference-report-full.json"), ReportJson("reference-runner", 100, 8.0, "Test.V1.Benchmark"));
        File.WriteAllText(Path.Combine(directory.Path, "candidate-report-full.json"), ReportJson("candidate-runner", 11, 9.0, "Test.V2.Benchmark"));

        var result = PerformanceGateEvaluator.Evaluate(
            Path.Combine(directory.Path, "baseline.json"),
            Path.Combine(directory.Path, "candidate-report-full.json"),
            paired: true,
            referenceResultsPath: Path.Combine(directory.Path, "reference-report-full.json"));

        Assert.False(result.Passed);
        Assert.Contains(result.Messages, message => message.StartsWith("FAIL paired reference and candidate benchmark environments differ.", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateBenchmarkFullNamesInOneReportAreRejected()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "baseline.json"), BaselineJson("1.0", 0));
        File.WriteAllText(
            Path.Combine(directory.Path, "report-full.json"),
            ReportJsonWithBenchmarks("1.0", ("Test.Benchmark", 1.0, 0), ("Test.Benchmark", 1.0, 0)));

        var exception = Assert.Throws<InvalidDataException>(() => PerformanceGateEvaluator.Evaluate(
            Path.Combine(directory.Path, "baseline.json"),
            directory.Path,
            paired: false));

        Assert.Contains("duplicate FullName 'Test.Benchmark'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateBenchmarkFullNamesAcrossReportFilesAreRejected()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "baseline.json"), BaselineJson("1.0", 0));
        File.WriteAllText(Path.Combine(directory.Path, "first-report-full.json"), ReportJson("1.0", 0));
        File.WriteAllText(Path.Combine(directory.Path, "second-report-full.json"), ReportJson("1.0", 0));

        var exception = Assert.Throws<InvalidDataException>(() => PerformanceGateEvaluator.Evaluate(
            Path.Combine(directory.Path, "baseline.json"),
            directory.Path,
            paired: false));

        Assert.Contains("duplicate FullName 'Test.Benchmark'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(1.0, -1.0)]
    public void NegativeBaselineMetricValuesAreRejected(double mean, double allocation)
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "baseline.json"), BaselineJson("1.0", allocation, mean));
        File.WriteAllText(Path.Combine(directory.Path, "report-full.json"), ReportJson("1.0", 0));

        Assert.Throws<InvalidDataException>(() => PerformanceGateEvaluator.Evaluate(
            Path.Combine(directory.Path, "baseline.json"),
            directory.Path,
            paired: false));
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(2.0, -1.0)]
    public void NegativeComparisonMetricValuesAreRejected(double comparisonMean, double comparisonAllocation)
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(
            Path.Combine(directory.Path, "baseline.json"),
            BaselineWithComparisonJson(comparisonMean, comparisonAllocation));
        File.WriteAllText(Path.Combine(directory.Path, "report-full.json"), ReportJson("1.0", 0, 2.0));

        Assert.Throws<InvalidDataException>(() => PerformanceGateEvaluator.Evaluate(
            Path.Combine(directory.Path, "baseline.json"),
            directory.Path,
            paired: false));
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(1.0, -1.0)]
    public void NegativeBenchmarkDotNetMetricValuesAreRejected(double mean, double allocation)
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "baseline.json"), BaselineJson("1.0", 0));
        File.WriteAllText(Path.Combine(directory.Path, "report-full.json"), ReportJson("1.0", allocation, mean));

        Assert.Throws<InvalidDataException>(() => PerformanceGateEvaluator.Evaluate(
            Path.Combine(directory.Path, "baseline.json"),
            directory.Path,
            paired: false));
    }

    private static string BaselineJson(string version, double allocation, double mean = 1.0) => $$"""
        {
          "environment": {
            "benchmarkdotnet_version": "{{version}}",
            "dotnet_cli_version": "10.0.201",
            "runtime_version": ".NET 10",
            "os_version": "test-os",
            "architecture": "X64",
            "processor": "test-cpu",
            "configuration": "RELEASE"
          },
          "gate": { "maximum_mean_regression_percent": 25.0 },
          "metrics": [
            {
              "id": "state.current",
              "benchmark": "Test.Benchmark",
              "mean_nanoseconds": {{mean}},
              "allocated_bytes_per_operation": {{allocation}},
              "require_zero_allocation": true
            }
          ]
        }
        """;

    private static string BaselineWithComparisonJson(double comparisonMean = 2.0, double comparisonAllocation = 0.0) => $$"""
        {
          "environment": {
            "benchmarkdotnet_version": "1.0",
            "dotnet_cli_version": "10.0.201",
            "runtime_version": ".NET 10",
            "os_version": "test-os",
            "architecture": "X64",
            "processor": "test-cpu",
            "configuration": "RELEASE"
          },
          "gate": { "maximum_mean_regression_percent": 25.0 },
          "metrics": [
            {
              "id": "request.dispatch.ping.v2",
              "benchmark": "Test.Benchmark",
              "mean_nanoseconds": 1.0,
              "allocated_bytes_per_operation": 0,
              "comparison": {
                "mean_nanoseconds": {{comparisonMean}},
                "allocated_bytes_per_operation": {{comparisonAllocation}}
              },
              "require_zero_allocation": true
            }
          ]
        }
        """;

    private static string MappedComparisonBaselineJson() => """
        {
          "environment": {
            "benchmarkdotnet_version": "developer-machine",
            "dotnet_cli_version": "10.0.201",
            "runtime_version": ".NET 10",
            "os_version": "test-os",
            "architecture": "X64",
            "processor": "test-cpu",
            "configuration": "RELEASE"
          },
          "gate": { "maximum_mean_regression_percent": 25.0 },
          "metrics": [
            {
              "id": "request.dispatch.ping.v2",
              "benchmark": "Test.V2.Benchmark",
              "mean_nanoseconds": 1.0,
              "allocated_bytes_per_operation": 1,
              "comparison": {
                "benchmark": "Test.V1.Benchmark",
                "mean_nanoseconds": 10.0,
                "allocated_bytes_per_operation": 10
              }
            }
          ]
        }
        """;

    private static string ReportJson(string version, double allocation, double mean = 1.0, string benchmark = "Test.Benchmark") =>
        ReportJsonWithBenchmarks(version, (benchmark, mean, allocation));

    private static string ReportJsonWithBenchmarks(string version, params (string Benchmark, double Mean, double Allocation)[] benchmarks)
    {
        var entries = string.Join(",", benchmarks.Select(benchmark => $$"""
            {
              "FullName": "{{benchmark.Benchmark}}",
              "Statistics": { "Mean": {{benchmark.Mean}} },
              "Memory": { "BytesAllocatedPerOperation": {{benchmark.Allocation}} }
            }
            """));

        return $$"""
        {
          "HostEnvironmentInfo": {
            "BenchmarkDotNetVersion": "{{version}}",
            "DotNetCliVersion": "10.0.201",
            "RuntimeVersion": ".NET 10",
            "OsVersion": "test-os",
            "Architecture": "X64",
            "ProcessorName": "test-cpu",
            "Configuration": "RELEASE"
          },
          "Benchmarks": [
            {{entries}}
          ]
        }
        """;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        internal string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Directory.CreateTempSubdirectory("mcsl-perf-gate-").FullName;
            return new TemporaryDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
