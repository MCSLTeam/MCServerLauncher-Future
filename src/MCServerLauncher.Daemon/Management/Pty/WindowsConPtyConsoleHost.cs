using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MCServerLauncher.Daemon.Management.Pty;

/// <summary>
/// Windows ConPTY host. Falls back to redirected pipes when ConPTY is unavailable.
/// </summary>
internal sealed class WindowsConPtyConsoleHost : IInstanceConsoleHost
{
    private readonly Process _process;
    private readonly Stream _input;
    private readonly Stream _output;
    private IntPtr _pseudoConsole;
    private readonly bool _isPty;

    private WindowsConPtyConsoleHost(
        Process process,
        Stream input,
        Stream output,
        IntPtr pseudoConsole,
        bool isPty)
    {
        _process = process;
        _input = input;
        _output = output;
        _pseudoConsole = pseudoConsole;
        _isPty = isPty;
        InputStream = input;
        OutputStream = output;
        ProcessId = process.Id;
    }

    public bool IsPty => _isPty;

    public Stream InputStream { get; }
    public Stream OutputStream { get; }
    public int? ProcessId { get; }
    public bool OwnsProcessLifecycle => true;


    public Process Process => _process;

    public static WindowsConPtyConsoleHost Start(ProcessStartInfo startInfo, ushort columns, ushort rows)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();

        try
        {
            return StartConPty(startInfo, columns, rows);
        }
        catch (Exception)
        {
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = false };
            if (!process.Start())
                throw new InvalidOperationException("Failed to start process.");
            return new WindowsConPtyConsoleHost(
                process,
                process.StandardInput.BaseStream,
                process.StandardOutput.BaseStream,
                IntPtr.Zero,
                isPty: false);
        }
    }

    private static WindowsConPtyConsoleHost StartConPty(ProcessStartInfo startInfo, ushort columns, ushort rows)
    {
        if (!CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (!CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        var size = new Coord(columns, rows);
        var hr = CreatePseudoConsole(size, inputRead, outputWrite, 0, out var hPC);
        if (hr != 0)
            throw new Win32Exception(hr);

        CloseHandle(inputRead);
        CloseHandle(outputWrite);

        var startupInfo = new StartupInfoEx();
        startupInfo.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();

        var attrSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
        startupInfo.lpAttributeList = Marshal.AllocHGlobal(attrSize);
        if (!InitializeProcThreadAttributeList(startupInfo.lpAttributeList, 1, 0, ref attrSize))
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        if (!UpdateProcThreadAttribute(
                startupInfo.lpAttributeList,
                0,
                (IntPtr)0x00020016,
                hPC,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        var cmd = string.IsNullOrEmpty(startInfo.Arguments)
            ? $"\"{startInfo.FileName}\""
            : $"\"{startInfo.FileName}\" {startInfo.Arguments}";

        var creationFlags = 0x00080000; // EXTENDED_STARTUPINFO_PRESENT
        if (!CreateProcess(
                null,
                cmd,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                creationFlags,
                IntPtr.Zero,
                string.IsNullOrEmpty(startInfo.WorkingDirectory) ? null : startInfo.WorkingDirectory,
                ref startupInfo,
                out var processInfo))
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        DeleteProcThreadAttributeList(startupInfo.lpAttributeList);
        Marshal.FreeHGlobal(startupInfo.lpAttributeList);
        CloseHandle(processInfo.hThread);

        var process = Process.GetProcessById(processInfo.dwProcessId);
            // process id stored below
        var inputStream = new FileStream(new SafeFileHandle(inputWrite, ownsHandle: true), FileAccess.Write);
        var outputStream = new FileStream(new SafeFileHandle(outputRead, ownsHandle: true), FileAccess.Read);
        return new WindowsConPtyConsoleHost(process, inputStream, outputStream, hPC, isPty: true);
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        => _input.WriteAsync(data, cancellationToken).AsTask();

    public void Write(ReadOnlyMemory<byte> data)
    {
        _input.Write(data.Span);
        _input.Flush();
    }

    public void WriteLine(string line, Encoding encoding)
    {
        var bytes = encoding.GetBytes(line.EndsWith("\n", StringComparison.Ordinal) ? line : line + "\n");
        Write(bytes);
    }

    public async Task WriteLineAsync(string line, Encoding encoding, CancellationToken cancellationToken)
    {
        var bytes = encoding.GetBytes(line + "\r\n");
        await _input.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Resize(ushort columns, ushort rows)
    {
        if (_pseudoConsole == IntPtr.Zero)
            return;
        _ = ResizePseudoConsole(_pseudoConsole, new Coord(columns, rows));
    }

    public void Dispose()
    {
        if (_pseudoConsole != IntPtr.Zero)
        {
            ClosePseudoConsole(_pseudoConsole);
            _pseudoConsole = IntPtr.Zero;
        }

        _input.Dispose();
        _output.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public short X;
        public short Y;

        public Coord(int x, int y)
        {
            X = (short)x;
            Y = (short)y;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(Coord size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(IntPtr hPC, Coord size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, IntPtr lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        int dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfoEx lpStartupInfo,
        out ProcessInformation lpProcessInformation);
}
