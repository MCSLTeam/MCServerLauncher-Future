using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Serilog;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public class Utils
{
    // PInvoke declarations
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("ntdll.dll")]
    static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass,
        ref ProcessBasicInformation processInformation, uint processInformationLength, ref uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int dwSize,
        out int lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

    // Struct to hold process information
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr Reserved3;
    }

    private static IEnumerable<string> GetEnvironmentVariablesWin(int pid)
    {
        IntPtr processHandle =
            OpenProcess(0x0010 | 0x0400 | 0x0008, false, pid); // PROCESS_QUERY_INFORMATION | PROCESS_VM_READ

        if (processHandle == IntPtr.Zero)
        {
            Console.WriteLine("Failed to open process.");
            return Enumerable.Empty<string>();
        }

        ProcessBasicInformation pbi = new ProcessBasicInformation();
        uint returnLength = 0;
        int status =
            NtQueryInformationProcess(processHandle, 0, ref pbi, (uint)Marshal.SizeOf(pbi), ref returnLength);

        if (status != 0)
        {
            Console.WriteLine("Failed to query process information.");
            CloseHandle(processHandle);
            return Enumerable.Empty<string>();
        }

        IntPtr pebAddress = pbi.PebBaseAddress;

        // Read PEB memory
        byte[] pebBuffer = new byte[IntPtr.Size];
        int bytesRead;
        if (!ReadProcessMemory(processHandle, pebAddress + 0x20 /* Offset for ProcessParameters */, pebBuffer,
                pebBuffer.Length, out bytesRead))
        {
            Console.WriteLine("Failed to read PEB.");
            CloseHandle(processHandle);
            return Enumerable.Empty<string>();
        }

        IntPtr processParametersAddress = (IntPtr)BitConverter.ToInt64(pebBuffer, 0);

        // Read environment variables block address
        byte[] environmentBuffer = new byte[IntPtr.Size];
        if (!ReadProcessMemory(processHandle, processParametersAddress + 0x80 /* Offset for Environment */,
                environmentBuffer, environmentBuffer.Length, out bytesRead))
        {
            Console.WriteLine("Failed to read process parameters.");
            CloseHandle(processHandle);
            return Enumerable.Empty<string>();
        }

        IntPtr environmentAddress = (IntPtr)BitConverter.ToInt64(environmentBuffer, 0);

        // Read the environment block (arbitrary large buffer to read environment variables)
        byte[] environmentData = new byte[0x4000]; // Adjust size if needed
        if (!ReadProcessMemory(processHandle, environmentAddress, environmentData, environmentData.Length,
                out bytesRead))
        {
            Console.WriteLine("Failed to read environment block.");
            CloseHandle(processHandle);
            return Enumerable.Empty<string>();
        }

        // Convert environment data to string and split by null terminators
        string environmentString = Encoding.Unicode.GetString(environmentData).Trim();

        // split \0\0
        environmentString = environmentString.Substring(0, FindEnvironStringEnd(environmentString));

        string[] environmentVariables =
            environmentString.Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);

        CloseHandle(processHandle);
        return environmentVariables;
    }

    /// <summary>
    ///  查找'\0''\0'的位置
    /// </summary>
    /// <param name="environ"></param>
    /// <returns></returns>
    private static int FindEnvironStringEnd(string environ)
    {
        var lastIndex = environ.IndexOf('\0');
        while (lastIndex != -1)
        {
            var index = environ.IndexOf('\0', lastIndex + 1);
            if (index == lastIndex + 1)
            {
                return lastIndex;
            }

            lastIndex = index;
        }

        return -1;
    }

    private static IEnumerable<string> GetEnvironmentVariablesLinux(int pid)
    {
        var process = Process.Start("cat", $"/proc/{pid}/environ");
        process?.WaitForExit();
        return process?.StandardOutput.ReadToEnd().Split('\0').ToList() ?? Enumerable.Empty<string>();
    }

    public static IEnumerable<string> GetEnvironmentVariables(int pid)
    {
        return GetEnvironmentVariablesWin(pid);
    }
}