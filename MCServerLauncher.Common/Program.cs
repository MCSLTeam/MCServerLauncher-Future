using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MCServerLauncher.Common
{
    public static class Common
    {
        // // PInvoke declarations
        // [DllImport("kernel32.dll", SetLastError = true)]
        // static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);
        //
        // [DllImport("ntdll.dll")]
        // static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass,
        //     ref ProcessBasicInformation processInformation, uint processInformationLength, ref uint returnLength);
        //
        // [DllImport("kernel32.dll", SetLastError = true)]
        // static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int dwSize,
        //     out int lpNumberOfBytesRead);
        //
        // [DllImport("kernel32.dll", SetLastError = true)]
        // static extern bool CloseHandle(IntPtr hObject);
        //
        // // Struct to hold process information
        // [StructLayout(LayoutKind.Sequential)]
        // private struct ProcessBasicInformation
        // {
        //     public IntPtr Reserved1;
        //     public IntPtr PebBaseAddress;
        //     public IntPtr Reserved2_0;
        //     public IntPtr Reserved2_1;
        //     public IntPtr UniqueProcessId;
        //     public IntPtr Reserved3;
        // }
        //
        // public static IEnumerable<string> GetEnvironmentVariablesWin(int pid)
        // {
        //     IntPtr processHandle =
        //         OpenProcess(0x0010 | 0x0400 | 0x0008, false, pid); // PROCESS_QUERY_INFORMATION | PROCESS_VM_READ
        //
        //     if (processHandle == IntPtr.Zero)
        //     {
        //         Console.WriteLine("Failed to open process.");
        //         return Enumerable.Empty<string>();
        //     }
        //
        //     ProcessBasicInformation pbi = new ProcessBasicInformation();
        //     uint returnLength = 0;
        //     int status =
        //         NtQueryInformationProcess(processHandle, 0, ref pbi, (uint)Marshal.SizeOf(pbi), ref returnLength);
        //
        //     if (status != 0)
        //     {
        //         Console.WriteLine("Failed to query process information.");
        //         CloseHandle(processHandle);
        //         return Enumerable.Empty<string>();
        //     }
        //
        //     IntPtr pebAddress = pbi.PebBaseAddress;
        //
        //     // Read PEB memory
        //     byte[] pebBuffer = new byte[IntPtr.Size];
        //     int bytesRead;
        //     if (!ReadProcessMemory(processHandle, pebAddress + 0x20 /* Offset for ProcessParameters */, pebBuffer,
        //             pebBuffer.Length, out bytesRead))
        //     {
        //         Console.WriteLine("Failed to read PEB.");
        //         CloseHandle(processHandle);
        //         return Enumerable.Empty<string>();
        //     }
        //
        //     IntPtr processParametersAddress = (IntPtr)BitConverter.ToInt64(pebBuffer, 0);
        //
        //     // Read environment variables block address
        //     byte[] environmentBuffer = new byte[IntPtr.Size];
        //     if (!ReadProcessMemory(processHandle, processParametersAddress + 0x80 /* Offset for Environment */,
        //             environmentBuffer, environmentBuffer.Length, out bytesRead))
        //     {
        //         Console.WriteLine("Failed to read process parameters.");
        //         CloseHandle(processHandle);
        //         return Enumerable.Empty<string>();
        //     }
        //
        //     IntPtr environmentAddress = (IntPtr)BitConverter.ToInt64(environmentBuffer, 0);
        //
        //     // Read the environment block (arbitrary large buffer to read environment variables)
        //     byte[] environmentData = new byte[0x4000]; // Adjust size if needed
        //     if (!ReadProcessMemory(processHandle, environmentAddress, environmentData, environmentData.Length,
        //             out bytesRead))
        //     {
        //         Console.WriteLine("Failed to read environment block.");
        //         CloseHandle(processHandle);
        //         return Enumerable.Empty<string>();
        //     }
        //
        //     // Convert environment data to string and split by null terminators
        //     string environmentString = Encoding.Unicode.GetString(environmentData);
        //
        //     environmentString = environmentString.Substring(0, environmentString.IndexOf("\0\0"));
        //
        //     string[] environmentVariables =
        //         environmentString.Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        //
        //     CloseHandle(processHandle);
        //     return environmentVariables;
        // }
        //
        // public static IEnumerable<string> GetEnvironmentVariablesLinux(int pid)
        // {
        //     var process = Process.Start("cat", $"/proc/{pid}/environ");
        //     process?.WaitForExit();
        //     return process?.StandardOutput.ReadToEnd().Split('\0').ToList() ?? Enumerable.Empty<string>();
        // }

        static void Main(string[] args)
        {
            var blank = Test();
            
            Console.Write("Enter the process ID: ");
            var pid = int.Parse(Console.ReadLine()!);
            var envs = Utils.GetEnvironmentVariables(pid);
            // Print all environment variables
            foreach (var env in envs)
            {
                Console.WriteLine(env);
            }
            
            if (blank != null)
            {
                blank.Kill();
                blank.WaitForExit();
            }
        }

        public static Process? Test()
        {
            try
            {
                var info = new ProcessStartInfo("proc_env.exe")
                {
                    Arguments = "--blank",
                    UseShellExecute = false
                };
                info.EnvironmentVariables.Add("TEST_DAEMON", "daemon");
                var p = Process.Start(info)!;
                Console.WriteLine("Start a blank process with env vars: TEST_DAEMON=daemon");
                Console.WriteLine(p.Id);
                return p;
            }
            catch
            {
                return null;
            }
        }
    }
}