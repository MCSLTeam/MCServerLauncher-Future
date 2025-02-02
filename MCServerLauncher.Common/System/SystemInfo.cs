using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace MCServerLauncher.Common.System;

public record struct OsInfo(string Name, string Arch);

public record struct CpuInfo(string Vendor, string Name, int Slots, int Count, double Usage);

public record struct MemInfo(ulong Total, ulong Free); // in KB

public interface ISystemInfo
{
    ValueTask<CpuInfo> GetCpuInfo();
    ValueTask<MemInfo> GetMemInfo();
}

public static class SystemInfoHelper
{
    public static async Task<string> RunCommandAsync(string command, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null) throw new InvalidOperationException($"Failed to start process: {command} {arguments}");
        var result = await process.StandardOutput.ReadToEndAsync();
        process.WaitForExit();
        return result.Trim();
    }
}

public static class SystemInfoExtensions
{
    public static Task<string> RunCommandAsync(this ISystemInfo _, string command, string arguments)
    {
        return SystemInfoHelper.RunCommandAsync(command, arguments);
    }

    public static OsInfo GetOsInfo(this ISystemInfo _)
    {
        return new OsInfo(Environment.OSVersion.ToString(), RuntimeInformation.OSArchitecture.ToString());
    }
}

public class WinSystemInfo : ISystemInfo
{
    public async ValueTask<CpuInfo> GetCpuInfo()
    {
        var vendorTask = this.RunCommandAsync(
                "powershell",
                "-c \"Get-CimInstance Win32_Processor | Select-Object Manufacturer\""
            )
            .MapResult(x =>
            {
                var lines = x.Split('\n');
                return lines.Length >= 3 ? lines[2].Trim() : "Unknown";
            });
        var nameTask = this.RunCommandAsync(
            "powershell",
            "-c \"Get-CimInstance Win32_Processor | Select-Object Name\""
        ).MapResult(x =>
        {
            var lines = x.Split('\n');
            return lines.Length >= 3 ? lines[2].Trim() : "Unknown";
        });

        var slotsTask = this.RunCommandAsync(
                "powershell",
                "-c \"Get-CimInstance Win32_Processor | Select-Object SocketDesignation\""
            )
            .MapResult(x => x.Split('\n').Length - 2);


        await Task.WhenAll(vendorTask, nameTask, slotsTask);
        return new CpuInfo(vendorTask.Result, nameTask.Result, slotsTask.Result, Environment.ProcessorCount,
            await GetCpuUsage());
    }

    public async ValueTask<MemInfo> GetMemInfo()
    {
        var totalMemTask = this.RunCommandAsync(
                "powershell",
                "-c \"Get-CimInstance Win32_ComputerSystem | Select-Object TotalPhysicalMemory\""
            )
            .MapResult(x => ulong.Parse(x.Split('\n')[2]));

        var freeMemTask = this.RunCommandAsync(
            "powershell",
            "-c \"Get-CimInstance Win32_OperatingSystem | Select-Object FreePhysicalMemory\""
        ).MapResult(x => ulong.Parse(x.Split('\n')[2]));

        await Task.WhenAll(totalMemTask, freeMemTask);

        return new MemInfo(totalMemTask.Result, freeMemTask.Result);
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime,
        out FILETIME lpUserTime);

    private static async Task<double> GetCpuUsage(int delay = 500)
    {
        var (idle1, total1) = GetCpuTime();
        await Task.Delay(delay);
        var (idle2, total2) = GetCpuTime();

        return total2 - total1 == 0 ? 0 : (1.0 - (double)(idle2 - idle1) / (total2 - total1)) * 100;
    }

    private static (ulong, ulong) GetCpuTime()
    {
        if (!GetSystemTimes(out var lpIdleTime, out var lpKernelTime, out var lpUserTime))
            throw new InvalidOperationException("Failed to get CPU times");

        var idleTime = ((ulong)lpIdleTime.dwHighDateTime << 32) | (uint)lpIdleTime.dwLowDateTime;
        var kernelTime = ((ulong)lpKernelTime.dwHighDateTime << 32) | (uint)lpKernelTime.dwLowDateTime;
        var userTime = ((ulong)lpUserTime.dwHighDateTime << 32) | (uint)lpUserTime.dwLowDateTime;

        return (idleTime, kernelTime + userTime);
    }
}

public class LinuxSystemInfo : ISystemInfo
{
    public async ValueTask<CpuInfo> GetCpuInfo()
    {
        var cpuInfo = File.ReadAllLines("/proc/cpuinfo");
        var name = cpuInfo[4].Split(':')[1].Trim();
        var vendor = cpuInfo[1].Split(':')[1].Trim();

        var slots = await this.RunCommandAsync(
                "sh",
                "-c \"grep 'physical id' /proc/cpuinfo | awk -F: '{print $2 | \\\"sort -un\\\"}' | wc -l\""
            )
            .MapResult(int.Parse);

        var usage = await GetCpuUsage();
        return new CpuInfo(vendor, name, slots, Environment.ProcessorCount, usage);
    }

    public async ValueTask<MemInfo> GetMemInfo()
    {
        var totalMemTask = this.RunCommandAsync("sh", "-c \"cat /proc/meminfo | grep MemTotal | awk '{print $2}'\"")
            .MapResult(ulong.Parse);
        var availableMemTask =
            this.RunCommandAsync("sh", "-c \"cat /proc/meminfo | grep MemAvailable | awk '{print $2}'\"")
                .MapResult(ulong.Parse);

        await Task.WhenAll(totalMemTask, availableMemTask);
        return new MemInfo(totalMemTask.Result, availableMemTask.Result);
    }

    private static Task<(ulong, ulong)> GetLinuxCpuTime()
    {
        return SystemInfoHelper.RunCommandAsync(
            "sh",
            "-c \"cat /proc/stat | grep '^cpu ' | awk '{total=$2+$3+$4+$5; idle=$5; print total,idle}'\""
        ).MapResult(x =>
        {
            var rv = x.Split(' ').Select(ulong.Parse).ToArray();
            return (rv[0], rv[1]);
        });
    }

    private static async Task<double> GetCpuUsage(int delay = 500)
    {
        var (total1, idle1) = await GetLinuxCpuTime();

        await Task.Delay(delay);

        var (total2, idle2) = await GetLinuxCpuTime();

        var totalDiff = total2 - total1;
        var idleDiff = idle2 - idle1;

        return totalDiff == 0 ? 0 : (1.0 - (double)idleDiff / totalDiff) * 100;
    }
}

public class MacOSSystemInfo : ISystemInfo
{
    public async ValueTask<CpuInfo> GetCpuInfo()
    {
        var cpuUsage =
            await this.RunCommandAsync("sh", "-c \"top -l 1 | grep 'CPU usage' | awk '{print $3}' | tr -d '%'\"")
                .MapResult(double.Parse);

        var name = await this.RunCommandAsync("sysctl", "-n machdep.cpu.brand_string");
        var vendor = name.Split(' ')[0];
        return new CpuInfo(vendor, name, 1, Environment.ProcessorCount, cpuUsage);
    }

    public async ValueTask<MemInfo> GetMemInfo()
    {
        var total = await this.RunCommandAsync("sysctl", "-n vm.pages").MapResult(ulong.Parse);
        var pageSize = await this.RunCommandAsync("getconf", "PAGESIZE").MapResult(ulong.Parse);
        pageSize /= 1024; // in KB

        var vmStatActivePages = await this
            .RunCommandAsync("sh", "-c \"vm_stat | grep 'Pages active' | awk '{print $3}'\"")
            .MapResult(s => ulong.Parse(s.Trim('.')));
        var vmStatWiredPages = await this
            .RunCommandAsync("sh", "-c \"vm_stat | grep 'Pages wired down' | awk '{print $4}'\"")
            .MapResult(s => ulong.Parse(s.Trim('.')));

        return new MemInfo(total * pageSize, (total - vmStatActivePages - vmStatWiredPages) * pageSize);
    }
}

public record struct SystemInfo(OsInfo Os, CpuInfo Cpu, MemInfo Mem)
{
    public static async Task<SystemInfo> Get()
    {
        ISystemInfo systemInfo = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new WinSystemInfo()
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? new MacOSSystemInfo()
                : new LinuxSystemInfo();
        return new SystemInfo(
            systemInfo.GetOsInfo(),
            await systemInfo.GetCpuInfo(),
            await systemInfo.GetMemInfo()
        );
    }
}

public class SystemInfoModel
{
    public OsInfo Os { get; set; }
    public CpuInfo Cpu { get; set; }
    public MemInfo Mem { get; set; }
}