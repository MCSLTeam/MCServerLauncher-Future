using System.ComponentModel;
using System.Runtime.InteropServices;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType.Status;
using Microsoft.Management.Infrastructure;

namespace MCServerLauncher.Daemon.Utils.Status;

public static class CpuInfoHelper
{
    public static async Task<CpuInfo> GetCpuInfo()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var (vendor, name) = await Task.Run(() =>
            {
                try
                {
                    var manufacturer = "Unknown";
                    var name = "Unknown";

                    using var session = CimSession.Create("localhost");
                    var instances = session.QueryInstances(
                        @"root\cimv2",
                        "WQL",
                        "SELECT Manufacturer, Name, SocketDesignation FROM Win32_Processor"
                    );

                    foreach (var instance in instances)
                        // 只取第一个 CPU 的制造商和名称（通常多路系统型号相同）
                        if (manufacturer == "Unknown")
                        {
                            manufacturer = instance.CimInstanceProperties["Manufacturer"]?.Value?.ToString()?.Trim() ??
                                           "Unknown";
                            name = instance.CimInstanceProperties["Name"]?.Value?.ToString()?.Trim() ?? "Unknown";
                        }

                    return (manufacturer, name);
                }
                // catch (CimException ex) when (ex.NativeErrorCode == 0x800706BA)
                // {
                //     // WinRM 未启用错误
                //     throw new InvalidOperationException(
                //         "需要启用 WinRM 服务：请在 PowerShell 中运行 'Enable-PSRemoting -Force'", ex);
                // }
                catch (CimException ex) when (ex.NativeErrorCode == NativeErrorCode.AccessDenied)
                {
                    // 权限不足
                    throw new UnauthorizedAccessException(
                        "需要以管理员权限运行程序", ex);
                }
            });

            return new CpuInfo(vendor, name, Environment.ProcessorCount, await GetWinCpuUsage());
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var cpuUsage =
                await SystemInfoHelper.RunCommandAsync("sh",
                        "-c \"top -l 1 | grep 'CPU usage' | awk '{print $3}' | tr -d '%'\"")
                    .MapResult(double.Parse);

            var name = await SystemInfoHelper.RunCommandAsync("sysctl", "-n machdep.cpu.brand_string");
            var vendor = name.Split(' ')[0];
            return new CpuInfo(vendor, name, Environment.ProcessorCount, cpuUsage);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var cpuInfo = await File.ReadAllLinesAsync("/proc/cpuinfo");
            var name = cpuInfo[4].Split(':')[1].Trim();
            var vendor = cpuInfo[1].Split(':')[1].Trim();

            var usage = await GetLinuxCpuUsage();
            return new CpuInfo(vendor, name, Environment.ProcessorCount, usage);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var cpuUsage =
                await SystemInfoHelper.RunCommandAsync("sh",
                        "-c \"top -l 1 | grep 'CPU usage' | awk '{print $3}' | tr -d '%'\"")
                    .MapResult(double.Parse);

            var name = await SystemInfoHelper.RunCommandAsync("sysctl", "-n machdep.cpu.brand_string");
            var vendor = name.Split(' ')[0];
            return new CpuInfo(vendor, name, Environment.ProcessorCount, cpuUsage);
        }

        throw new NotSupportedException("Unsupported OS");
    }

    private static Task<(ulong, ulong)> GetLinuxCpuTime()
    {
        return SystemInfoHelper.RunCommandAsync(
            "sh",
            "-c \"cat /proc/stat | grep '^cpu ' | awk '{total=0; for(i=2;i<=NF;i++) total+=$i; idle=$5+$6; print total, idle}'\""
        ).MapResult(x =>
        {
            var rv = x.Split(' ').Select(ulong.Parse).ToArray();
            return (rv[0], rv[1]);
        });
    }

    private static async Task<double> GetLinuxCpuUsage(int delay = 500)
    {
        var (total1, idle1) = await GetLinuxCpuTime();

        await Task.Delay(delay);

        var (total2, idle2) = await GetLinuxCpuTime();

        var totalDiff = total2 - total1;
        var idleDiff = idle2 - idle1;

        return totalDiff == 0 ? 0 : (1.0 - (double)idleDiff / totalDiff) * 100;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime,
        out FILETIME lpKernelTime,
        out FILETIME lpUserTime);

    public static async Task<double> GetWinCpuUsage(int samplingInterval = 200)
    {
        // 第一次采样
        var (idleTime1, totalTime1) = GetWinCpuTime();

        // 异步等待采样间隔
        await Task.Delay(samplingInterval);

        // 第二次采样
        var (idleTime2, totalTime2) = GetWinCpuTime();

        // 计算差值
        var idleDelta = idleTime2 - idleTime1;
        var totalDelta = totalTime2 - totalTime1;

        // 处理除零情况
        if (totalDelta == 0) return 0;

        // 计算使用率
        return 100.0 * (1.0 - (double)idleDelta / totalDelta);
    }

    private static (ulong IdleTime, ulong TotalTime) GetWinCpuTime()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(
                error, "获取系统时间失败");
        }

        // 转换时间值为 UInt64
        var idle = ((ulong)idleTime.dwHighDateTime << 32) | idleTime.dwLowDateTime;
        var kernel = ((ulong)kernelTime.dwHighDateTime << 32) | kernelTime.dwLowDateTime;
        var user = ((ulong)userTime.dwHighDateTime << 32) | userTime.dwLowDateTime;

        // 关键修正：内核时间已包含空闲时间，总时间 = 内核时间 + 用户时间
        return (idle, kernel + user);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }
}