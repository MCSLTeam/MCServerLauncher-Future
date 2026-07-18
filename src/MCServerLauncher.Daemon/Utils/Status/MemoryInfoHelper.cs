using System.Runtime.InteropServices;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.Contracts.System;
using Microsoft.Management.Infrastructure;
using Serilog;

namespace MCServerLauncher.Daemon.Utils.Status;

public static class MemoryInfoHelper
{
    private static readonly CimSession? Session =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? CimSession.Create("localhost") : null;

    public static readonly ulong TotalPhysicalMemory;

    static MemoryInfoHelper()
    {
        TotalPhysicalMemory = GetTotalPhysicalMemory();
    }

    private static ulong GetTotalPhysicalMemory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Session != null)
            try
            {
                var instances = Session.QueryInstances(
                    @"root\cimv2",
                    "WQL",
                    "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"
                ).ToArray();
                var instance = instances.FirstOrDefault();
                var total = instance?.CimInstanceProperties["TotalPhysicalMemory"]?.Value as ulong? ?? 0UL;

                foreach (var queryInstance in instances) queryInstance.Dispose();

                return total / 1024;
            }
            catch (CimException ex)
            {
                Log.Warning($"CIM query failed: {ex.Message} ({ex.NativeErrorCode})");
                ex.Dispose();
                return 0UL;
            }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var task = SystemInfoHelper.RunCommandAsync("sh", "-c \"awk '/MemTotal/ {print $2}' /proc/meminfo\"");
            task.Wait();
            if (!ulong.TryParse(task.Result.Trim(), out var totalKb))
                throw new InvalidOperationException("Failed to parse total memory");
            return totalKb;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var task = SystemInfoHelper.RunCommandAsync("sysctl", "-n hw.memsize");
            task.Wait();
            if (!ulong.TryParse(task.Result.Trim(), out var total))
                throw new InvalidOperationException("Failed to parse total memory");
            return total / 1024;
        }

        throw new PlatformNotSupportedException("Unsupported OS");
    }

    public static Task<MemoryInfo> GetMemInfo()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Prefer GlobalMemoryStatusEx (no CIM thread hop per request).
            if (TryGetWindowsAvailableMemoryKb(out var availableKb))
            {
                return Task.FromResult(new MemoryInfo(TotalPhysicalMemory, availableKb));
            }

            if (Session != null)
            {
                try
                {
                    var instances = Session.QueryInstances(
                        @"root\cimv2",
                        "WQL",
                        "SELECT FreePhysicalMemory FROM Win32_OperatingSystem"
                    ).ToArray();
                    var instance = instances.FirstOrDefault();
                    var freeKb = instance?.CimInstanceProperties["FreePhysicalMemory"]?.Value as ulong? ?? 0UL;
                    foreach (var queryInstance in instances) queryInstance.Dispose();
                    return Task.FromResult(new MemoryInfo(TotalPhysicalMemory, freeKb));
                }
                catch (CimException ex)
                {
                    Log.Warning($"CIM query failed: {ex.Message} ({ex.NativeErrorCode})");
                    ex.Dispose();
                    return Task.FromResult(new MemoryInfo(TotalPhysicalMemory, 0UL));
                }
            }

            return Task.FromResult(new MemoryInfo(TotalPhysicalMemory, 0UL));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Task.FromResult(ReadLinuxMemoryInfo());
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return GetMacOsMemoryInfoAsync();
        }

        throw new PlatformNotSupportedException("Unsupported OS");
    }

    private static MemoryInfo ReadLinuxMemoryInfo()
    {
        ulong availableKb = 0;
        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            if (!line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !ulong.TryParse(parts[1], out availableKb))
            {
                throw new InvalidOperationException("Failed to parse available memory");
            }

            return new MemoryInfo(TotalPhysicalMemory, availableKb);
        }

        throw new InvalidOperationException("Failed to parse available memory");
    }

    private static async Task<MemoryInfo> GetMacOsMemoryInfoAsync()
    {
        var pageSize = await SystemInfoHelper.RunCommandAsync("getconf", "PAGESIZE").MapTask(ulong.Parse);
        var pageSizeKb = pageSize / 1024;
        var pagesFree = await SystemInfoHelper
            .RunCommandAsync("sh", "-c \"vm_stat | grep 'Pages free' | awk '{print $3}'\"")
            .MapTask(s => ulong.Parse(s.Trim('.')));
        var pagesInactive = await SystemInfoHelper
            .RunCommandAsync("sh", "-c \"vm_stat | grep 'Pages inactive' | awk '{print $3}'\"")
            .MapTask(s => ulong.Parse(s.Trim('.')));
        var freeKb = (pagesFree + pagesInactive) * pageSizeKb;
        return new MemoryInfo(TotalPhysicalMemory, freeKb);
    }

    private static bool TryGetWindowsAvailableMemoryKb(out ulong availableKb)
    {
        availableKb = 0;
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            return false;
        }

        availableKb = status.ullAvailPhys / 1024;
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
