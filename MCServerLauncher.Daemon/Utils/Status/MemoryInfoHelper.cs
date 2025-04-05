using System.Runtime.InteropServices;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Common.Utils;
using Microsoft.Management.Infrastructure;
using Serilog;

namespace MCServerLauncher.Daemon.Utils.Status;

public static class MemoryInfoHelper
{
    public static async Task<MemInfo> GetMemInfo()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var session = CimSession.Create("localhost");

                    // 同步获取总物理内存B
                    var total = session.QueryInstances(
                            @"root\cimv2",
                            "WQL",
                            "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"
                        ).FirstOrDefault()?
                        .CimInstanceProperties["TotalPhysicalMemory"]?
                        .Value as ulong? ?? 0UL;

                    // 同步获取空闲物理内存KB
                    var freeKb = session.QueryInstances(
                            @"root\cimv2",
                            "WQL",
                            "SELECT FreePhysicalMemory FROM Win32_OperatingSystem"
                        ).FirstOrDefault()?
                        .CimInstanceProperties["FreePhysicalMemory"]?
                        .Value as ulong? ?? 0UL;

                    return new MemInfo(total / 1024, freeKb);
                }
                catch (CimException ex)
                {
                    Log.Warning($"CIM query failed: {ex.Message} ({ex.NativeErrorCode})");
                    return new MemInfo(0, 0);
                }
            });
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // 单次获取所有内存信息
            var memInfoRaw = await SystemInfoHelper.RunCommandAsync("sh",
                    "-c \"awk '/MemTotal|MemAvailable/ {print $2}' /proc/meminfo\"")
                .ConfigureAwait(false);

            var parts = memInfoRaw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // 确保获取到两个值
            if (parts.Length != 2 ||
                !ulong.TryParse(parts[0], out var totalKb) ||
                !ulong.TryParse(parts[1], out var availableKb))
            {
                throw new InvalidOperationException("Failed to parse memory info");
            }

            return new MemInfo(totalKb, availableKb);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // 获取总内存（字节）并转换为 KB
            var totalBytes = await SystemInfoHelper.RunCommandAsync("sysctl", "-n hw.memsize").MapResult(ulong.Parse);
            var totalKb = totalBytes / 1024;

            // 获取页面大小（字节）并转换为 KB
            var pageSize = await SystemInfoHelper.RunCommandAsync("getconf", "PAGESIZE").MapResult(ulong.Parse);
            var pageSizeKb = pageSize / 1024; // 例如 4096 / 1024 = 4

            // 获取空闲页面和非活跃页面数
            var pagesFree = await SystemInfoHelper
                .RunCommandAsync("sh", "-c \"vm_stat | grep 'Pages free' | awk '{print $3}'\"")
                .MapResult(s => ulong.Parse(s.Trim('.')));
            var pagesInactive = await SystemInfoHelper
                .RunCommandAsync("sh", "-c \"vm_stat | grep 'Pages inactive' | awk '{print $3}'\"")
                .MapResult(s => ulong.Parse(s.Trim('.')));

            // 计算可用内存（KB）
            var availablePages = pagesFree + pagesInactive;
            var freeKb = availablePages * pageSizeKb;

            return new MemInfo(totalKb, freeKb);
        }
        
        throw new PlatformNotSupportedException("Unsupported OS");
    }
}