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

    public static Dictionary<int, int> BuildProcessTree()
    {
        var tree = new Dictionary<int, int>();
        foreach (var p in Process.GetProcesses())
            try
            {
                tree[p.Id] = GetParentProcessId(p.Id);
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
                    if (proc.ProcessName.Equals("java", StringComparison.OrdinalIgnoreCase))
                    {
                        var cmdLine = GetCommandLine(pid);
                        if (cmdLine.Contains(partialCmdLine))
                            return pid;
                    }
                }
                catch
                {
                    /* 忽略错误 */
                }

        Log.Warning("[ProcessTreeHelper] Could not find subprocess with partialCmdLine={0} of Process(pid={1})",
            partialCmdLine, wrapperPid);
        return -1;
    }

    private static string GetCommandLine(int pid)
    {
        using var session = CimSession.Create("localhost");
        var query = $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}";
        foreach (var instance in session.QueryInstances("root\\cimv2", "WQL", query))
        {
            var prop = instance.CimInstanceProperties["CommandLine"];
            if (prop?.Value is not null) return prop.Value.ToString() ?? string.Empty;
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