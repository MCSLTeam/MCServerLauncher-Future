using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Management.Infrastructure;
using Serilog;

namespace MCServerLauncher.Daemon.Utils.Status;

public static class ProcessInfo
{
    public static async Task<(long MemoryUsage, double CpuUsage)> GetProcessUsageAsync(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var memoryUsage = await GetMemoryUsageAsync(process);
            var cpuUsage = await GetCpuUsageAsync(process);
            return (memoryUsage, cpuUsage);
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Error retrieving process info: {ex.Message}");
            return (-1, -1);
        }
    }

    private static async Task<long> GetMemoryUsageAsync(Process process)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var pid = process.Id;
                return await Task.Run(() =>
                {
                    try
                    {
                        using var session = CimSession.Create("localhost");
                        var query =
                            $"SELECT WorkingSetPrivate FROM Win32_PerfFormattedData_PerfProc_Process WHERE IDProcess = {pid}";
                        var instances = session.QueryInstances("root/cimv2", "WQL", query);

                        foreach (var instance in instances)
                        {
                            var prop = instance.CimInstanceProperties["WorkingSetPrivate"];
                            if (prop?.Value != null) return Convert.ToInt64(prop.Value);
                        }

                        return 0;
                    }
                    catch (CimException ex)
                    {
                        Log.Warning("Can't get memory info of process(pid={0}): {1}", pid, ex.Message);
                        return -1;
                    }
                });
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux: 读取 /proc/[pid]/status 的 VmRSS
                var statusPath = $"/proc/{process.Id}/status";
                if (File.Exists(statusPath))
                {
                    var lines = await File.ReadAllLinesAsync(statusPath);
                    foreach (var line in lines)
                        if (line.StartsWith("VmRSS:"))
                        {
                            // VmRSS 单位为 kB，转换为字节
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            return long.Parse(parts[1]) * 1024;
                        }
                }
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ps",
                    Arguments = $"-p {process.Id} -o rss=",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };

                using var ps = Process.Start(psi);
                if (ps == null)
                    throw new InvalidOperationException($"Failed to start ps for PID {process.Id}");

                var output = await ps.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                await ps.WaitForExitAsync().ConfigureAwait(false);

                return long.Parse(output.Trim()) * 1024;
            }
        }
        catch (Exception e)
        {
            Log.Warning("Can't get memory info of process(pid={0}): {1}", process.Id, e.Message);
        }

        return -1;
    }

    private static async Task<double> GetCpuUsageAsync(Process process)
    {
        try
        {
            var startTime = DateTime.Now;
            var startCpuTime = process.TotalProcessorTime;

            // 等待 500ms 以提高精度
            await Task.Delay(500);

            process.Refresh();
            var endTime = DateTime.Now;
            var endCpuTime = process.TotalProcessorTime;

            var cpuTimeUsed = (endCpuTime - startCpuTime).TotalMilliseconds;
            var elapsedTime = (endTime - startTime).TotalMilliseconds;

            // 计算 CPU 使用率（百分比）
            var cpuUsage = cpuTimeUsed / (elapsedTime * Environment.ProcessorCount) * 100;

            return Math.Round(cpuUsage, 2);
        }
        catch
        {
            return -1;
        }
    }
}