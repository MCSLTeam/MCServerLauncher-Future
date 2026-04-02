using System.Diagnostics;

namespace MCServerLauncher.ProtocolTests.Helpers;

public readonly record struct PerformanceMeasurement(double NanosecondsPerOperation, double AllocatedBytesPerOperation);

public static class PerformanceGateHarness
{
    public static PerformanceMeasurement Measure(
        Action operation,
        int operationsPerSample,
        int warmupSamples,
        int measuredSamples)
    {
        if (operationsPerSample <= 0)
            throw new ArgumentOutOfRangeException(nameof(operationsPerSample));
        if (warmupSamples < 0)
            throw new ArgumentOutOfRangeException(nameof(warmupSamples));
        if (measuredSamples <= 0)
            throw new ArgumentOutOfRangeException(nameof(measuredSamples));

        for (var i = 0; i < warmupSamples; i++)
        {
            RunSample(operation, operationsPerSample);
        }

        var timeSamples = new double[measuredSamples];
        var allocationSamples = new double[measuredSamples];

        for (var i = 0; i < measuredSamples; i++)
        {
            ForceFullCollection();

            var (elapsed, allocatedBytes) = RunSample(operation, operationsPerSample);
            timeSamples[i] = elapsed.TotalNanoseconds / operationsPerSample;
            allocationSamples[i] = (double)allocatedBytes / operationsPerSample;
        }

        return new PerformanceMeasurement(
            Median(timeSamples),
            Median(allocationSamples));
    }

    private static (TimeSpan Elapsed, long AllocatedBytes) RunSample(Action operation, int operationsPerSample)
    {
        var startAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
        var startTimestamp = Stopwatch.GetTimestamp();

        for (var i = 0; i < operationsPerSample; i++)
        {
            operation();
        }

        var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - startAllocatedBytes;
        return (elapsed, allocatedBytes);
    }

    private static void ForceFullCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        var middle = values.Length / 2;

        if (values.Length % 2 == 0)
            return (values[middle - 1] + values[middle]) / 2.0;

        return values[middle];
    }
}
