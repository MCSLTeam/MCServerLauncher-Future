namespace MCServerLauncher.Common.ProtoType.Status;

using System;
using System.Linq;
using System.Text.Json.Serialization;

public record OsInfo(string Name, string Arch);

public record CpuInfo(string Vendor, string Name, int Count, double Usage);

public record MemInfo(ulong Total, ulong Free); // in KB

public record DriveInformation(string DriveFormat, ulong Total, ulong Free, string Name = ""); // in Byte

public record SystemInfo(OsInfo Os, CpuInfo Cpu, MemInfo Mem, DriveInformation Drive)
{
    [JsonConstructor]
    public SystemInfo(
        OsInfo os,
        CpuInfo cpu,
        MemInfo mem,
        DriveInformation drive,
        DriveInformation[]? drives,
        string? daemonVersion)
        : this(os, cpu, mem, drive)
    {
        Drives = drives is { Length: > 0 } ? drives : [drive];
        DaemonVersion = daemonVersion;
    }

    public DriveInformation[] Drives { get; init; } = [Drive];
    public string? DaemonVersion { get; init; }

    public virtual bool Equals(SystemInfo? other)
    {
        return other is not null
               && EqualityContract == other.EqualityContract
               && Os == other.Os
               && Cpu == other.Cpu
               && Mem == other.Mem
               && Drive == other.Drive
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
        foreach (var drive in Drives)
        {
            hash.Add(drive);
        }

        hash.Add(DaemonVersion);
        return hash.ToHashCode();
    }
}
