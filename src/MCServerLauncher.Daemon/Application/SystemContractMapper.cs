using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.System;
using LegacyJavaRuntime = MCServerLauncher.Common.ProtoType.JavaInfo;
using LegacySystemInfo = MCServerLauncher.Common.ProtoType.Status.SystemInfo;
using LegacyCpuInfo = MCServerLauncher.Common.ProtoType.Status.CpuInfo;
using LegacyDriveInfo = MCServerLauncher.Common.ProtoType.Status.DriveInformation;
using LegacyMemInfo = MCServerLauncher.Common.ProtoType.Status.MemInfo;
using LegacyOsInfo = MCServerLauncher.Common.ProtoType.Status.OsInfo;
using ContractDriveInfo = MCServerLauncher.Common.Contracts.System.DriveInfo;

namespace MCServerLauncher.Daemon.ApplicationCore;

internal static class SystemContractMapper
{
    internal static SystemInfo ToContract(LegacySystemInfo systemInfo)
    {
        return new SystemInfo(
            new OperatingSystemInfo(systemInfo.Os.Name, systemInfo.Os.Arch),
            new ProcessorInfo(
                systemInfo.Cpu.Vendor,
                systemInfo.Cpu.Name,
                systemInfo.Cpu.Count,
                systemInfo.Cpu.Usage,
                systemInfo.Cpu.CoreCount,
                systemInfo.Cpu.ThreadCount),
            new MemoryInfo(systemInfo.Mem.Total, systemInfo.Mem.Free),
            new ContractDriveInfo(
                systemInfo.Drive.DriveFormat,
                systemInfo.Drive.Total,
                systemInfo.Drive.Free,
                systemInfo.Drive.Name),
            systemInfo.Drives.Select(drive => new ContractDriveInfo(
                drive.DriveFormat,
                drive.Total,
                drive.Free,
                drive.Name)).ToImmutableArray(),
            systemInfo.DaemonVersion);
    }

    internal static JavaRuntimeList ToContract(IEnumerable<LegacyJavaRuntime> javaRuntimes)
    {
        return new JavaRuntimeList(javaRuntimes
            .Select(java => new JavaRuntime(java.Path, java.Version, java.Architecture))
            .ToImmutableArray());
    }

    internal static LegacySystemInfo ToLegacy(SystemInfo systemInfo)
    {
        var drives = systemInfo.Drives.Select(drive => new LegacyDriveInfo(
            drive.DriveFormat,
            drive.TotalBytes,
            drive.FreeBytes,
            drive.Name)).ToArray();
        var drive = new LegacyDriveInfo(
            systemInfo.Drive.DriveFormat,
            systemInfo.Drive.TotalBytes,
            systemInfo.Drive.FreeBytes,
            systemInfo.Drive.Name);
        return new LegacySystemInfo(
            new LegacyOsInfo(systemInfo.Os.Name, systemInfo.Os.Architecture),
            new LegacyCpuInfo(
                systemInfo.Cpu.Vendor,
                systemInfo.Cpu.Name,
                systemInfo.Cpu.Count,
                systemInfo.Cpu.Usage,
                systemInfo.Cpu.CoreCount,
                systemInfo.Cpu.ThreadCount),
            new LegacyMemInfo(systemInfo.Mem.TotalKilobytes, systemInfo.Mem.FreeKilobytes),
            drive,
            drives,
            systemInfo.DaemonVersion);
    }

    internal static LegacyJavaRuntime[] ToLegacy(JavaRuntimeList javaRuntimes)
    {
        return javaRuntimes.Items
            .Select(java => new LegacyJavaRuntime(java.Path, java.Version, java.Architecture))
            .ToArray();
    }
}
