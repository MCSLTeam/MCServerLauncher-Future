using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.Contracts.System;
using Microsoft.Management.Infrastructure;

namespace MCServerLauncher.Daemon.Utils.Status;

public static class CpuInfoHelper
{
    private static readonly CimSession? Session =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? CimSession.Create("localhost") : null;

    public static readonly string Vendor;
    public static readonly string Name;
    public static readonly int ProcessorCount = Environment.ProcessorCount;
    public static readonly int CoreCount;
    public static readonly int ThreadCount;

    static CpuInfoHelper()
    {
        var (name, vendor, coreCount, threadCount) = GetCpuInfoSnapshot();
        Name = name;
        Vendor = vendor;
        CoreCount = coreCount;
        ThreadCount = threadCount;
    }

    private static (string Name, string Vendor, int CoreCount, int ThreadCount) GetCpuInfoSnapshot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var manufacturer = "Unknown";
            var name = "Unknown";
            var coreCount = 0;
            var threadCount = 0;

            var instances = Session!.QueryInstances(
                @"root\cimv2",
                "WQL",
                "SELECT Manufacturer, Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"
            ).ToArray();

            foreach (var instance in instances)
            {
                // 只取第一个 CPU 的制造商和名称（通常多路系统型号相同）
                if (manufacturer == "Unknown")
                {
                    manufacturer = instance.CimInstanceProperties["Manufacturer"]?.Value?.ToString()?.Trim() ??
                                   "Unknown";
                    name = instance.CimInstanceProperties["Name"]?.Value?.ToString()?.Trim() ?? "Unknown";
                }

                coreCount += ReadCimInt(instance, "NumberOfCores");
                threadCount += ReadCimInt(instance, "NumberOfLogicalProcessors");
            }

            foreach (var instance in instances) instance.Dispose();

            return (name, manufacturer, NormalizeProcessorCount(coreCount), NormalizeProcessorCount(threadCount));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var cpuInfo = File.ReadAllLines("/proc/cpuinfo");
            var name = ReadLinuxCpuField(cpuInfo, "model name") ?? "Unknown";
            var vendor = ReadLinuxCpuField(cpuInfo, "vendor_id") ?? "Unknown";
            var coreCount = GetLinuxPhysicalCoreCount(cpuInfo);
            return (name, vendor, coreCount, ProcessorCount);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var task = SystemInfoHelper.RunCommandAsync("sysctl", "-n machdep.cpu.brand_string");
            task.Wait();
            var name = task.Result.Trim();
            var vendor = name.Split(' ')[0];
            return (name, vendor, GetMacOsProcessorCount("hw.physicalcpu"), GetMacOsProcessorCount("hw.logicalcpu"));
        }

        throw new NotSupportedException("Unsupported OS");
    }

    public static async Task<ProcessorInfo> GetCpuInfo()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return CreateCpuInfo(await GetWinCpuUsage());

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Vendor/Name are static; only sample usage on the hot path.
            var cpuUsage =
                await SystemInfoHelper.RunCommandAsync("sh",
                        "-c \"top -l 1 | grep 'CPU usage' | awk '{print $3}' | tr -d '%'\"")
                    .MapTask(ParseMacOsCpuUsage);
            return CreateCpuInfo(cpuUsage);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var usage = await GetLinuxCpuUsage();
            return CreateCpuInfo(usage);
        }

        throw new NotSupportedException("Unsupported OS");
    }

    internal static double ParseMacOsCpuUsage(string output)
    {
        return double.TryParse(
            output.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var usage)
            ? usage
            : 0;
    }

    private static ProcessorInfo CreateCpuInfo(double usage)
    {
        return new ProcessorInfo(Vendor, Name, ThreadCount, usage, CoreCount, ThreadCount);
    }

    private static int ReadCimInt(CimInstance instance, string propertyName)
    {
        var value = instance.CimInstanceProperties[propertyName]?.Value;
        return value switch
        {
            int intValue => intValue,
            uint uintValue => checked((int)uintValue),
            ushort ushortValue => ushortValue,
            _ => int.TryParse(value?.ToString(), out var parsed) ? parsed : 0
        };
    }

    private static string? ReadLinuxCpuField(string[] cpuInfo, string fieldName)
    {
        var prefix = fieldName + "\t:";
        return cpuInfo.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?
            .Split(':', 2)[1]
            .Trim();
    }

    private static int GetLinuxPhysicalCoreCount(string[] cpuInfo)
    {
        var physicalCores = new HashSet<string>(StringComparer.Ordinal);
        var physicalId = string.Empty;
        var coreId = string.Empty;

        foreach (var line in cpuInfo)
        {
            if (line.Length == 0)
            {
                AddLinuxCore(physicalCores, physicalId, coreId);
                physicalId = string.Empty;
                coreId = string.Empty;
                continue;
            }

            if (line.StartsWith("physical id", StringComparison.Ordinal))
            {
                physicalId = line.Split(':', 2)[1].Trim();
            }
            else if (line.StartsWith("core id", StringComparison.Ordinal))
            {
                coreId = line.Split(':', 2)[1].Trim();
            }
        }

        AddLinuxCore(physicalCores, physicalId, coreId);
        return NormalizeProcessorCount(physicalCores.Count);
    }

    private static void AddLinuxCore(HashSet<string> physicalCores, string physicalId, string coreId)
    {
        if (string.IsNullOrWhiteSpace(physicalId) || string.IsNullOrWhiteSpace(coreId)) return;
        physicalCores.Add($"{physicalId}:{coreId}");
    }

    private static int GetMacOsProcessorCount(string key)
    {
        try
        {
            var task = SystemInfoHelper.RunCommandAsync("sysctl", $"-n {key}");
            task.Wait();
            return int.TryParse(task.Result.Trim(), out var count)
                ? NormalizeProcessorCount(count)
                : ProcessorCount;
        }
        catch
        {
            return ProcessorCount;
        }
    }

    private static int NormalizeProcessorCount(int value)
    {
        return value > 0 ? value : Math.Max(1, ProcessorCount);
    }

    private static readonly object LinuxCpuSampleGate = new();
    private static ulong _linuxLastTotal;
    private static ulong _linuxLastIdle;
    private static double _linuxLastUsage;
    private static bool _linuxHasSample;

    private static (ulong Total, ulong Idle) ReadLinuxCpuTime()
    {
        // Avoid shelling out on every sample; parse /proc/stat directly.
        using var reader = new StreamReader("/proc/stat");
        var line = reader.ReadLine();
        if (line is null || !line.StartsWith("cpu ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Failed to read /proc/stat cpu line.");
        }

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // cpu user nice system idle iowait irq softirq steal ...
        ulong total = 0;
        for (var i = 1; i < parts.Length; i++)
        {
            total += ulong.Parse(parts[i], CultureInfo.InvariantCulture);
        }

        var idle = ulong.Parse(parts[4], CultureInfo.InvariantCulture);
        if (parts.Length > 5)
        {
            idle += ulong.Parse(parts[5], CultureInfo.InvariantCulture); // iowait
        }

        return (total, idle);
    }

    private static async Task<double> GetLinuxCpuUsage(int delay = 300)
    {
        var sample = ReadLinuxCpuTime();
        lock (LinuxCpuSampleGate)
        {
            if (_linuxHasSample)
            {
                var totalDiff = sample.Total - _linuxLastTotal;
                var idleDiff = sample.Idle - _linuxLastIdle;
                _linuxLastTotal = sample.Total;
                _linuxLastIdle = sample.Idle;
                if (totalDiff > 0)
                {
                    _linuxLastUsage = (1.0 - (double)idleDiff / totalDiff) * 100;
                }

                return _linuxLastUsage;
            }
        }

        // Cold start: take a short interval so the first post-boot sample is meaningful.
        await Task.Delay(delay).ConfigureAwait(false);
        var second = ReadLinuxCpuTime();
        lock (LinuxCpuSampleGate)
        {
            var totalDiff = second.Total - sample.Total;
            var idleDiff = second.Idle - sample.Idle;
            _linuxLastTotal = second.Total;
            _linuxLastIdle = second.Idle;
            _linuxHasSample = true;
            _linuxLastUsage = totalDiff == 0 ? 0 : (1.0 - (double)idleDiff / totalDiff) * 100;
            return _linuxLastUsage;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime,
        out FILETIME lpKernelTime,
        out FILETIME lpUserTime);

    private static readonly object WinCpuSampleGate = new();
    private static ulong _winLastIdle;
    private static ulong _winLastTotal;
    private static double _winLastUsage;
    private static bool _winHasSample;

    public static async Task<double> GetWinCpuUsage(int samplingInterval = 300)
    {
        // Sliding two-sample window: after the first observation, each call is O(1) with no delay.
        // Concurrent system_info RPC no longer serializes behind Task.Delay(300).
        var sample = GetWinCpuTime();
        lock (WinCpuSampleGate)
        {
            if (_winHasSample)
            {
                var idleDelta = sample.IdleTime - _winLastIdle;
                var totalDelta = sample.TotalTime - _winLastTotal;
                _winLastIdle = sample.IdleTime;
                _winLastTotal = sample.TotalTime;
                if (totalDelta > 0)
                {
                    _winLastUsage = 100.0 * (1.0 - (double)idleDelta / totalDelta);
                }

                return _winLastUsage;
            }
        }

        await Task.Delay(samplingInterval).ConfigureAwait(false);
        var second = GetWinCpuTime();
        lock (WinCpuSampleGate)
        {
            var idleDelta = second.IdleTime - sample.IdleTime;
            var totalDelta = second.TotalTime - sample.TotalTime;
            _winLastIdle = second.IdleTime;
            _winLastTotal = second.TotalTime;
            _winHasSample = true;
            _winLastUsage = totalDelta == 0 ? 0 : 100.0 * (1.0 - (double)idleDelta / totalDelta);
            return _winLastUsage;
        }
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
