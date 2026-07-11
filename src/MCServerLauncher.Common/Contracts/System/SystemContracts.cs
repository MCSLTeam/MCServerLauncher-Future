using System.Collections.Immutable;

namespace MCServerLauncher.Common.Contracts.System;

public sealed record OperatingSystemInfo(string Name, string Architecture);

public sealed record ProcessorInfo(
    string Vendor,
    string Name,
    int Count,
    double Usage,
    int CoreCount,
    int ThreadCount);

public sealed record MemoryInfo(ulong TotalKilobytes, ulong FreeKilobytes);

public sealed record DriveInfo(string DriveFormat, ulong TotalBytes, ulong FreeBytes, string Name);

public sealed record SystemInfo(
    OperatingSystemInfo Os,
    ProcessorInfo Cpu,
    MemoryInfo Mem,
    DriveInfo Drive,
    ImmutableArray<DriveInfo> Drives,
    string? DaemonVersion);

public sealed record JavaRuntime(string Path, string Version, string Architecture);

public sealed record JavaRuntimeList(ImmutableArray<JavaRuntime> Items);
