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

    private static string BaselineJson(string version, double allocation) => $$"""
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
              "mean_nanoseconds": 1.0,
              "allocated_bytes_per_operation": {{allocation}},
              "require_zero_allocation": true
            }
          ]
        }
        """;

    private static string BaselineWithComparisonJson() => """
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
              "comparison": { "mean_nanoseconds": 2.0 },
              "require_zero_allocation": true
            }
          ]
        }
        """;

    private static string ReportJson(string version, double allocation, double mean = 1.0) => $$"""
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
            {
              "FullName": "Test.Benchmark",
              "Statistics": { "Mean": {{mean}} },
              "Memory": { "BytesAllocatedPerOperation": {{allocation}} }
            }
          ]
        }
        """;

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
