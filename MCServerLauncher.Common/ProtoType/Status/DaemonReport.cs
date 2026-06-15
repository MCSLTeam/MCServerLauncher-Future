namespace MCServerLauncher.Common.ProtoType.Status;

using System;
using System.Linq;
using System.Text.Json.Serialization;

public record DaemonReport(OsInfo Os, CpuInfo Cpu, MemInfo Mem, DriveInformation Drive, long StartTimeStamp)
{
    [JsonConstructor]
    public DaemonReport(
        OsInfo os,
        CpuInfo cpu,
        MemInfo mem,
        DriveInformation drive,
        long startTimeStamp,
        DriveInformation[]? drives,
        string? daemonVersion)
        : this(os, cpu, mem, drive, startTimeStamp)
    {
        Drives = drives is { Length: > 0 } ? drives : [drive];
        DaemonVersion = daemonVersion;
    }

    public DriveInformation[] Drives { get; init; } = [Drive];
    public string? DaemonVersion { get; init; }

    public virtual bool Equals(DaemonReport? other)
    {
        return other is not null
               && EqualityContract == other.EqualityContract
               && Os == other.Os
               && Cpu == other.Cpu
               && Mem == other.Mem
               && Drive == other.Drive
               && StartTimeStamp == other.StartTimeStamp
               && Drives.SequenceEqual(other.Drives)
               && DaemonVersion == other.DaemonVersion;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EqualityContract);
        hash.Add(Os);
        hash.Add(Cpu);
        hash.Add(Mem);
        hash.Add(Drive);
        hash.Add(StartTimeStamp);
        foreach (var drive in Drives)
        {
            hash.Add(drive);
        }

        hash.Add(DaemonVersion);
        return hash.ToHashCode();
    }
}
