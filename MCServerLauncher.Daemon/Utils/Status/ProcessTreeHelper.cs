using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Management.Infrastructure;
using Serilog;

namespace MCServerLauncher.Daemon.Utils.Status;

public static class ProcessTreeHelper
{
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation,
        int processInformationLength,
        out int returnLength
    );

    public static int GetParentProcessId(int pid)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using (var process = Process.GetProcessById(pid))
            {
                var pbi = new PROCESS_BASIC_INFORMATION();
                var status = NtQueryInformationProcess(
                    process.Handle,
                    0, // ProcessBasicInformation
                    ref pbi,
                    Marshal.SizeOf(pbi),
                    out _
                );

                if (status != 0)
                    throw new Win32Exception(status);

                return pbi.InheritedFromUniqueProcessId.ToInt32();
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var statusPath = $"/proc/{pid}/stat";
            if (File.Exists(statusPath))
            {
                var statContent = File.ReadAllText(statusPath);
                var parts = statContent.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 3)
                {
                    if (int.TryParse(parts[3], out var ppid))
                    {
                        return ppid;
                    }
                }
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ps",
                Arguments = $"-p {pid} -o ppid=",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using var ps = Process.Start(psi);
            if (ps != null)
            {
                var output = ps.StandardOutput.ReadToEnd();
                ps.WaitForExit();
                if (int.TryParse(output.Trim(), out var ppid))
                {
                    return ppid;
                }
            }
        }

        return -1;
    }

    public static Dictionary<int, int> BuildProcessTree()
    {
        var tree = new Dictionary<int, int>();
        foreach (var p in Process.GetProcesses())
            try
            {
                var ppid = GetParentProcessId(p.Id);
                if (ppid != -1)
                {
                    tree[p.Id] = ppid;
                }
            }
            catch
            {
                /* 忽略错误 */
            }

        return tree;
    }

    public static int FindSubProcessPid(int wrapperPid, string partialCmdLine)
    {
        var tree = BuildProcessTree();
        foreach (var (pid, parentPid) in tree)
            if (parentPid == wrapperPid)
                try
                {
                    var proc = Process.GetProcessById(pid);
                    // Check if it's the target process (e.g. java) or if it's the wrapper itself
                    var cmdLine = GetCommandLine(pid);
                    if (cmdLine.Contains(partialCmdLine) || proc.ProcessName.Equals("java", StringComparison.OrdinalIgnoreCase))
                        return pid;
                }
                catch
                {
                    /* 忽略错误 */
                }

        // If we couldn't find a child process, maybe the wrapper itself is what we want
        try
        {
            var proc = Process.GetProcessById(wrapperPid);
            var cmdLine = GetCommandLine(wrapperPid);
            if (cmdLine.Contains(partialCmdLine) || proc.ProcessName.Equals("java", StringComparison.OrdinalIgnoreCase))
                return wrapperPid;
        }
        catch
        {
            /* 忽略错误 */
        }

        Log.Warning("[ProcessTreeHelper] Could not find subprocess with partialCmdLine={0} of Process(pid={1})",
            partialCmdLine, wrapperPid);
        return wrapperPid; // Fallback to wrapper pid instead of -1
    }

    private static string GetCommandLine(int pid)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                using var session = CimSession.Create("localhost");
                var query = $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}";
                foreach (var instance in session.QueryInstances("root\\cimv2", "WQL", query))
                {
                    var prop = instance.CimInstanceProperties["CommandLine"];
                    if (prop?.Value is not null) return prop.Value.ToString() ?? string.Empty;
                }
            }
            catch
            {
                /* 忽略错误 */
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var cmdlinePath = $"/proc/{pid}/cmdline";
            if (File.Exists(cmdlinePath))
            {
                try
                {
                    var cmdline = File.ReadAllText(cmdlinePath);
                    return cmdline.Replace('\0', ' ').Trim();
                }
                catch
                {
                    /* 忽略错误 */
                }
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ps",
                    Arguments = $"-p {pid} -o command=",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };

                using var ps = Process.Start(psi);
                if (ps != null)
                {
                    var output = ps.StandardOutput.ReadToEnd();
                    ps.WaitForExit();
                    return output.Trim();
                }
            }
            catch
            {
                /* 忽略错误 */
            }
        }

        return string.Empty;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }
}