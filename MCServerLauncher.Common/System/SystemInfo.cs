using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Newtonsoft.Json;

namespace MCServerLauncher.Common.System;

public record struct OsInfo(string Name, string Arch);

public record struct CpuInfo(string Vendor, string Name, int Count, double Usage);

public record struct MemInfo(ulong Total, ulong Free); // in KB

public interface ISystemInfo
{
    ValueTask<CpuInfo> GetCpuInfo();
    ValueTask<MemInfo> GetMemInfo();
}

public static class SystemInfoExtensions
{
    public static async Task<string> RunCommandAsync(this ISystemInfo _, string command, string arguments)
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

    public static OsInfo GetOsInfo(this ISystemInfo _)
    {
        return new OsInfo(Environment.OSVersion.ToString(), RuntimeInformation.OSArchitecture.ToString());
    }
}

public class WinSystemInfo : ISystemInfo
{
    public async ValueTask<CpuInfo> GetCpuInfo()
    {
        var vendor = "Unknown";
        var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
        foreach (var obj in searcher.Get())
        {
            vendor = obj["Manufacturer"]?.ToString() ?? "Unknown";
            break;
        }

        return new CpuInfo(vendor, GetCpuName(), Environment.ProcessorCount, await GetCpuUsage());
    }

    public ValueTask<MemInfo> GetMemInfo()
    {
        ulong total = 0;
        ulong free = 0;
        var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
        foreach (var obj in searcher.Get())
        {
            total = (ulong)obj["TotalVisibleMemorySize"]; // TotalVisibleMemorySize is in KB
            free = (ulong)obj["FreePhysicalMemory"]; // FreePhysicalMemory is in KB
        }

        return new ValueTask<MemInfo>(new MemInfo(total, free));
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FileTime lpIdleTime, out FileTime lpKernelTime,
        out FileTime lpUserTime);

    private static async Task<double> GetCpuUsage(int delay = 500)
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            throw new InvalidOperationException("Failed to get CPU times");

        var idle = ((ulong)idleTime.dwHighDateTime << 32) | idleTime.dwLowDateTime;
        var kernel = ((ulong)kernelTime.dwHighDateTime << 32) | kernelTime.dwLowDateTime;
        var user = ((ulong)userTime.dwHighDateTime << 32) | userTime.dwLowDateTime;

        // Wait a short interval for a more accurate calculation
        await Task.Delay(delay);

        if (!GetSystemTimes(out var idleTime2, out var kernelTime2, out var userTime2))
            throw new InvalidOperationException("Failed to get CPU times");

        var idle2 = ((ulong)idleTime2.dwHighDateTime << 32) | idleTime2.dwLowDateTime;
        var kernel2 = ((ulong)kernelTime2.dwHighDateTime << 32) | kernelTime2.dwLowDateTime;
        var user2 = ((ulong)userTime2.dwHighDateTime << 32) | userTime2.dwLowDateTime;

        var idleDiff = idle2 - idle;
        var totalDiff = kernel2 + user2 - (kernel + user);

        return totalDiff == 0 ? 0 : (1.0 - (double)idleDiff / totalDiff) * 100;
    }

    private static string GetCpuName()
    {
        var name = "Unknown";
        var searcher = new ManagementObjectSearcher("select Name from Win32_Processor");
        foreach (var obj in searcher.Get()) name = obj["Name"]?.ToString() ?? "Unknown";
        return name;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }
}

public class LinuxSystemInfo : ISystemInfo
{
    public async ValueTask<CpuInfo> GetCpuInfo()
    {
        var cpuInfo = File.ReadAllText("/proc/cpuinfo").Split('\n');
        var name = cpuInfo[4].Split(':')[1].Trim();
        var vendor = cpuInfo[1].Split(':')[1].Trim();

        var usage = await GetCpuUsage();
        return new CpuInfo(vendor, name, Environment.ProcessorCount, usage);
    }

    public async ValueTask<MemInfo> GetMemInfo()
    {
        var total = await this.RunCommandAsync("sh", "-c \"free -b | grep Mem | awk '{print $2}'\"");
        var free = await this.RunCommandAsync("sh", "-c \"free -b | grep Mem | awk '{print $4}'\"");
        return new MemInfo(ulong.Parse(total.Trim()), ulong.Parse(free.Trim()));
    }

    private static (ulong, ulong) GetLinuxCpuTime()
    {
        string[] lines = File.ReadAllLines("/proc/stat");
        var cpuLine = Array.Find(lines, line => line.StartsWith("cpu "));

        if (cpuLine == null)
            throw new InvalidOperationException("/proc/stat not found or invalid");

        var cpuStats = cpuLine.Split(new char[' '], StringSplitOptions.RemoveEmptyEntries);
        var user = ulong.Parse(cpuStats[1]);
        var nice = ulong.Parse(cpuStats[2]);
        var system = ulong.Parse(cpuStats[3]);
        var idle = ulong.Parse(cpuStats[4]);

        var total = user + nice + system + idle;
        return (total, idle);
    }

    private static async Task<double> GetCpuUsage(int delay = 500)
    {
        var (total1, idle1) = GetLinuxCpuTime();

        await Task.Delay(delay);

        var (total2, idle2) = GetLinuxCpuTime();

        var totalDiff = total2 - total1;
        var idleDiff = idle2 - idle1;

        return totalDiff == 0 ? 0 : (1.0 - (double)idleDiff / totalDiff) * 100;
    }
}

public class MacosSystemInfo : ISystemInfo
{
    public async ValueTask<CpuInfo> GetCpuInfo()
    {
        var cpuUsage =
            await this.RunCommandAsync("sh", "-c \"top -l 1 | grep 'CPU usage' | awk '{print $3}' | tr -d '%'\"")
                .MapResult(double.Parse);
        
        var name = await this.RunCommandAsync("sysctl", "-n machdep.cpu.brand_string");
        var vendor = name.Split(' ')[0];
        return new CpuInfo(vendor, name, Environment.ProcessorCount, cpuUsage);
    }

    public async ValueTask<MemInfo> GetMemInfo()
    {
        var total = await this.RunCommandAsync("sysctl", "-n vm.pages").MapResult(ulong.Parse);
        var pageSize = await this.RunCommandAsync("getconf", "PAGESIZE").MapResult(ulong.Parse);
        pageSize /= 1024; // in KB
        
        var vmStatActivePages = await this.RunCommandAsync("sh", "-c \"vm_stat | grep 'Pages active' | awk '{print $3}'\"").MapResult(s=>ulong.Parse(s.Trim('.')));
        var vmStatWiredPages = await this.RunCommandAsync("sh", "-c \"vm_stat | grep 'Pages wired down' | awk '{print $4}'\"").MapResult(s=>ulong.Parse(s.Trim('.')));
        
        return new MemInfo(total*pageSize, (total -vmStatActivePages-vmStatWiredPages)*pageSize);
    }
}

public record struct SystemInfo(OsInfo Os, CpuInfo Cpu, MemInfo Mem)
{
    public static async Task<SystemInfo> Get()
    {
        ISystemInfo systemInfo = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new WinSystemInfo()
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? new MacosSystemInfo()
                : new LinuxSystemInfo();
        return new SystemInfo(
            systemInfo.GetOsInfo(),
            await systemInfo.GetCpuInfo(),
            await systemInfo.GetMemInfo()
        );
    }
}