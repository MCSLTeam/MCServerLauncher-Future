using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.ProtocolTests.Fixtures.Persistence;
using MCServerLauncher.ProtocolTests.Helpers;
using StjJsonSerializer = System.Text.Json.JsonSerializer;

namespace MCServerLauncher.ProtocolTests;

[Collection("PerformanceGate")]
public class DaemonTransportAndPersistencePerformanceGateTests
{
    private static readonly Type InstanceConfigType =
        Type.GetType("MCServerLauncher.Common.ProtoType.Instance.InstanceConfig, MCServerLauncher.Common", throwOnError: true)!;

    private const double MaxRegressionRatio = 0.05;
    private const int WarmupSamples = 4;
    private const int MeasuredSamples = 7;

    private static readonly HotPathBaseline PersistenceReadWriteBaseline = new(
        NanosecondsPerOperation: 97_000,
        AllocatedBytesPerOperation: 16_700);

    [Fact]
    [Trait("Category", "Perf")]
    [Trait("Category", "PerformanceGate")]
    [Trait("Category", "AllocationGate")]
    public void PerfGate_PersistenceReadWriteRepresentative_DoesNotRegressMoreThanFivePercent()
    {
        var fixturePath = Path.Combine(PersistenceFixturePaths.InstanceConfigDir, "event-rule-heavy-daemon-instance.json");
        var fixtureJson = File.ReadAllText(fixturePath);
        var instanceConfig = DeserializePersistenceFixture(fixtureJson);

        var precheckSerialized = StjJsonSerializer.Serialize(instanceConfig, InstanceConfigType, DaemonPersistenceJsonBoundary.StjWriteIndentedOptions);
        Assert.NotEmpty(precheckSerialized);

        const int operationsPerSample = 500;
        var measurement = PerformanceGateHarness.Measure(
            operation: () =>
            {
                var loaded = StjJsonSerializer.Deserialize(fixtureJson, InstanceConfigType, DaemonPersistenceJsonBoundary.StjOptions);
                if (loaded is null)
                    throw new InvalidOperationException("Persistence replay sample read returned null");

                var serialized = StjJsonSerializer.Serialize(loaded, InstanceConfigType, DaemonPersistenceJsonBoundary.StjWriteIndentedOptions);
                if (serialized.Length == 0)
                    throw new InvalidOperationException("Persistence replay sample write serialized empty payload");
            },
            operationsPerSample: operationsPerSample,
            warmupSamples: WarmupSamples,
            measuredSamples: MeasuredSamples);

        AssertWithinThreshold("persistence representative read/write", measurement, PersistenceReadWriteBaseline);
    }

    private static object DeserializePersistenceFixture(string fixtureJson)
    {
        var value = StjJsonSerializer.Deserialize(fixtureJson, InstanceConfigType, DaemonPersistenceJsonBoundary.StjOptions);
        return value ?? throw new InvalidOperationException("Failed to deserialize persistence fixture instance config");
    }

    private static void AssertWithinThreshold(string scenarioName, PerformanceMeasurement actual, HotPathBaseline baseline)
    {
        var maxTime = baseline.NanosecondsPerOperation * (1 + MaxRegressionRatio);
        var maxAlloc = baseline.AllocatedBytesPerOperation * (1 + MaxRegressionRatio);

        Assert.True(
            actual.NanosecondsPerOperation <= maxTime,
            $"Perf regression for {scenarioName}: {actual.NanosecondsPerOperation:F2} ns/op > {maxTime:F2} ns/op (baseline {baseline.NanosecondsPerOperation:F2}, threshold {MaxRegressionRatio:P0})");

        Assert.True(
            actual.AllocatedBytesPerOperation <= maxAlloc,
            $"Allocation regression for {scenarioName}: {actual.AllocatedBytesPerOperation:F2} B/op > {maxAlloc:F2} B/op (baseline {baseline.AllocatedBytesPerOperation:F2}, threshold {MaxRegressionRatio:P0})");
    }

    private readonly record struct HotPathBaseline(double NanosecondsPerOperation, double AllocatedBytesPerOperation);
}
