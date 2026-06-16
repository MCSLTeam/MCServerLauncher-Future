namespace MCServerLauncher.Common.ProtoType.Instance;

public readonly record struct InstancePerformanceCounter
{
    [System.Text.Json.Serialization.JsonConstructor]
    public InstancePerformanceCounter(double cpu, long memory)
    {
        Cpu = NormalizeCpu(cpu);
        Memory = Math.Max(0, memory);
    }

    public double Cpu { get; }

    public long Memory { get; }

    private static double NormalizeCpu(double cpu)
    {
        if (double.IsNaN(cpu) || double.IsInfinity(cpu))
        {
            return 0;
        }

        return Math.Clamp(cpu, 0, 100);
    }
}
