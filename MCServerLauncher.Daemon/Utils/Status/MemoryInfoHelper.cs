using System.Runtime.InteropServices;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType.Status;
using Microsoft.Management.Infrastructure;
using Serilog;

namespace MCServerLauncher.Daemon.Utils.Status;

public static class MemoryInfoHelper
{
    private static readonly CimSession Session = CimSession.Create("localhost");
    public static readonly ulong TotalPhysicalMemory;

    static MemoryInfoHelper()
    {
        TotalPhysicalMemory = GetTotalPhysicalMemory();
    }

    private static ulong GetTotalPhysicalMemory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var instances = Session.QueryInstances(
                    @"root\cimv2",
                    "WQL",
                    "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"
                ).ToArray();
                var total = instances.FirstOrDefault()?
                    .CimInstanceProperties["TotalPhysicalMemory"]?
                    .Value as ulong? ?? 0UL;

                foreach (var queryInstance in instances)
                {
                    queryInstance.Dispose();
                }

                return total / 1024;
            }
            catch (CimException ex)
            {
                Log.Warning($"CIM query failed: {ex.Message} ({ex.NativeErrorCode})");
                ex.Dispose();
                return 0UL;
            }
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

    public static async Task<MemInfo> GetMemInfo()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var available = await Task.Run(() =>
            {
                try
                {
                    // 同步获取空闲物理内存KB
                    var instances = Session.QueryInstances(
                        @"root\cimv2",
                        "WQL",
                        "SELECT FreePhysicalMemory FROM Win32_OperatingSystem"
                    ).ToArray();
                    var freeKb = instances.FirstOrDefault()?
                        .CimInstanceProperties["FreePhysicalMemory"]?
                        .Value as ulong? ?? 0UL;

                    foreach (var queryInstance in instances)
                    {
                        queryInstance.Dispose();
                    }

                    return freeKb;
                }
                catch (CimException ex)
                {
                    Log.Warning($"CIM query failed: {ex.Message} ({ex.NativeErrorCode})");
                    ex.Dispose();
                    return 0UL;
                }
            });

            return new MemInfo(TotalPhysicalMemory, available);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var availableMemRaw = await SystemInfoHelper.RunCommandAsync("sh",
                    "-c \"awk '/MemAvailable/ {print $2}' /proc/meminfo\"")
                .ConfigureAwait(false);
            if (!ulong.TryParse(availableMemRaw.Trim(), out var availableKb))
                throw new InvalidOperationException("Failed to parse available memory");
            return new MemInfo(TotalPhysicalMemory, availableKb);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // 获取页面大小（字节）并转换为 KB
            var pageSize = await SystemInfoHelper.RunCommandAsync("getconf", "PAGESIZE").MapTask(ulong.Parse);
            var pageSizeKb = pageSize / 1024; // 例如 4096 / 1024 = 4

            // 获取空闲页面和非活跃页面数
            var pagesFree = await SystemInfoHelper
                .RunCommandAsync("sh", "-c \"vm_stat | grep 'Pages free' | awk '{print $3}'\"")
                .MapTask(s => ulong.Parse(s.Trim('.')));
            var pagesInactive = await SystemInfoHelper
                .RunCommandAsync("sh", "-c \"vm_stat | grep 'Pages inactive' | awk '{print $3}'\"")
                .MapTask(s => ulong.Parse(s.Trim('.')));

            // 计算可用内存（KB）
            var availablePages = pagesFree + pagesInactive;
            var freeKb = availablePages * pageSizeKb;

            return new MemInfo(TotalPhysicalMemory, freeKb);
        }

        throw new PlatformNotSupportedException("Unsupported OS");
    }
}