using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MCServerLauncher.Daemon.Management.Pty;

/// <summary>
/// Unix PTY host via forkpty(3). Parent owns the master FD; child execs the target with a controlling TTY.
/// Read and write use separate FDs (dup) so a blocking pump Read never starves keyboard Write.
/// </summary>
internal sealed class UnixPtyConsoleHost : IInstanceConsoleHost
{
    private readonly SafeFileHandle _masterRead;
    private readonly SafeFileHandle? _masterWriteHandle;
    private readonly FileStream _readStream;
    private readonly FileStream _writeStream;
    private readonly Process _process;
    // Serialize writers only (binary console + WriteLine); never share a lock with blocking reads.
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private UnixPtyConsoleHost(
        SafeFileHandle masterRead,
        FileStream readStream,
        SafeFileHandle? masterWriteHandle,
        FileStream writeStream,
        Process process)
    {
        _masterRead = masterRead;
        _masterWriteHandle = masterWriteHandle;
        _readStream = readStream;
        _writeStream = writeStream;
        _process = process;
        InputStream = writeStream;
        OutputStream = readStream;
        ProcessId = process.Id;
    }

    public bool IsPty => true;

    public Stream InputStream { get; }

    public Stream OutputStream { get; }
    public int? ProcessId { get; }
    public bool OwnsProcessLifecycle => true;

    public Process Process => _process;

    public void NotifyProcessExited()
    {
    }

    public static UnixPtyConsoleHost CreateViaForkPty(ProcessStartInfo startInfo, ushort columns, ushort rows)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsFreeBSD())
            throw new PlatformNotSupportedException("Unix PTY host requires a Unix-like OS.");

        var winsize = new Winsize { Rows = rows, Cols = columns };
        var pid = ForkPty(out var masterFd, ref winsize);
        if (pid < 0)
            throw new InvalidOperationException($"forkpty failed: {Marshal.GetLastPInvokeError()}");

        if (pid == 0)
        {
            try
            {
                var term = startInfo.Environment.TryGetValue("TERM", out var existingTerm) &&
                           !string.IsNullOrWhiteSpace(existingTerm)
                    ? existingTerm
                    : "xterm-256color";
                Environment.SetEnvironmentVariable("TERM", term);
                // Prefer truecolor when log colorizers / jline check COLORTERM.
                if (!startInfo.Environment.TryGetValue("COLORTERM", out var colorTerm) ||
                    string.IsNullOrWhiteSpace(colorTerm))
                {
                    Environment.SetEnvironmentVariable("COLORTERM", "truecolor");
                }

                Environment.SetEnvironmentVariable("COLUMNS", columns.ToString());
                Environment.SetEnvironmentVariable("LINES", rows.ToString());

                foreach (var pair in startInfo.Environment)
                {
                    if (pair.Value is not null)
                        Environment.SetEnvironmentVariable(pair.Key, pair.Value);
                }

                if (!string.IsNullOrEmpty(startInfo.WorkingDirectory))
                    Directory.SetCurrentDirectory(startInfo.WorkingDirectory);

                var argv = BuildArgv(startInfo);
                execvp(argv[0]!, argv);
            }
            catch
            {
                // fall through to _exit
            }

            _exit(127);
        }

        // forkpty master FDs are opened synchronously; isAsync:true throws on macOS/Linux.
        // Dup so read pump and write path never share one FileStream / one lock across blocking I/O.
        var writeFd = dup(masterFd);
        if (writeFd < 0)
            throw new InvalidOperationException($"dup(master) failed: {Marshal.GetLastPInvokeError()}");

        var masterRead = new SafeFileHandle((IntPtr)masterFd, ownsHandle: true);
        var masterWrite = new SafeFileHandle((IntPtr)writeFd, ownsHandle: true);
        FileStream? readStream = null;
        FileStream? writeStream = null;
        try
        {
            // PTY master is O_RDWR; keep ReadWrite access on both dups (direction is usage-only).
            readStream = new FileStream(masterRead, FileAccess.ReadWrite, bufferSize: 4096, isAsync: false);
            writeStream = new FileStream(masterWrite, FileAccess.ReadWrite, bufferSize: 1, isAsync: false);
            var process = Process.GetProcessById(pid);
            // Required for reliable WaitForExitAsync on processes not started via Process.Start.
            try
            {
                process.EnableRaisingEvents = true;
            }
            catch
            {
            }

            return new UnixPtyConsoleHost(masterRead, readStream, masterWrite, writeStream, process);
        }
        catch
        {
            writeStream?.Dispose();
            readStream?.Dispose();
            masterWrite.Dispose();
            masterRead.Dispose();
            throw;
        }
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writeStream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            // PTY master is a sync FD; buffered writes must flush or input appears stuck.
            await _writeStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Write(ReadOnlyMemory<byte> data)
    {
        _writeGate.Wait();
        try
        {
            _writeStream.Write(data.Span);
            _writeStream.Flush();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void WriteLine(string line, Encoding encoding)
    {
        var bytes = encoding.GetBytes(line.EndsWith("\n", StringComparison.Ordinal) ? line : line + "\n");
        Write(bytes);
    }

    public async Task WriteLineAsync(string line, Encoding encoding, CancellationToken cancellationToken)
    {
        var bytes = encoding.GetBytes(line + "\n");
        await WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    public void Resize(ushort columns, ushort rows)
    {
        var fd = _masterRead.DangerousGetHandle().ToInt32();
        var ws = new Winsize { Rows = rows, Cols = columns };
        _ = ioctl(fd, IoTioCswinsz, ref ws);
    }

    public void Dispose()
    {
        // FileStream owns the SafeFileHandle; dispose streams only.
        try
        {
            _writeStream.Dispose();
        }
        catch
        {
        }

        try
        {
            _readStream.Dispose();
        }
        catch
        {
        }

        try
        {
            _writeGate.Dispose();
        }
        catch
        {
        }

        _ = _masterWriteHandle;
        _ = _masterRead;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private static string?[] BuildArgv(ProcessStartInfo startInfo)
    {
        if (startInfo.ArgumentList.Count > 0)
        {
            var list = new string?[startInfo.ArgumentList.Count + 2];
            list[0] = startInfo.FileName;
            for (var i = 0; i < startInfo.ArgumentList.Count; i++)
                list[i + 1] = startInfo.ArgumentList[i];
            list[^1] = null;
            return list;
        }

        var parts = new List<string?> { startInfo.FileName };
        if (!string.IsNullOrWhiteSpace(startInfo.Arguments))
            parts.AddRange(SplitArgs(startInfo.Arguments));
        parts.Add(null);
        return parts.ToArray();
    }

    private static IEnumerable<string> SplitArgs(string args)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuote = false;
        foreach (var c in args)
        {
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            result.Add(current.ToString());
        return result;
    }

    // Linux TIOCSWINSZ = 0x5414; macOS = 0x80087467
    private static ulong IoTioCswinsz => OperatingSystem.IsMacOS() ? 0x80087467UL : 0x5414UL;

    [StructLayout(LayoutKind.Sequential)]
    private struct Winsize
    {
        public ushort Rows;
        public ushort Cols;
        public ushort XPixel;
        public ushort YPixel;
    }

    private static int ForkPty(out int amaster, ref Winsize winp)
    {
        // macOS exports forkpty from libutil; Linux from libc/libutil.
        return OperatingSystem.IsMacOS()
            ? forkpty_libutil(out amaster, IntPtr.Zero, IntPtr.Zero, ref winp)
            : forkpty_libc(out amaster, IntPtr.Zero, IntPtr.Zero, ref winp);
    }

    [DllImport("libutil", EntryPoint = "forkpty", SetLastError = true)]
    private static extern int forkpty_libutil(out int amaster, IntPtr name, IntPtr termp, ref Winsize winp);

    [DllImport("libc", EntryPoint = "forkpty", SetLastError = true)]
    private static extern int forkpty_libc(out int amaster, IntPtr name, IntPtr termp, ref Winsize winp);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref Winsize arg);

    [DllImport("libc", SetLastError = true)]
    private static extern int dup(int oldfd);

    [DllImport("libc", SetLastError = true)]
    private static extern int execvp([MarshalAs(UnmanagedType.LPUTF8Str)] string file, string?[] args);

    [DllImport("libc")]
    private static extern void _exit(int status);
}
