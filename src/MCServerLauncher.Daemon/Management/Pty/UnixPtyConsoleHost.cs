using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MCServerLauncher.Daemon.Management.Pty;

/// <summary>
/// Unix PTY host built on <c>posix_openpt</c> + <c>posix_spawn</c>. Parent owns the master FD; the
/// spawned child gets the slave as its controlling terminal.
/// Read and write use separate FDs (dup) so a blocking pump Read never starves keyboard Write.
/// </summary>
/// <remarks>
/// This deliberately does not use <c>forkpty</c>. After <c>fork()</c> in a multi-threaded CLR
/// process, only async-signal-safe calls are legal in the child, but the child has to build argv,
/// copy the environment and chdir — all managed code. Running that managed code in the forked child
/// crashed the process with SIGSEGV on Linux, so the child is now created by <c>posix_spawn</c>,
/// which performs the same setup inside libc without re-entering the runtime.
/// </remarks>
internal sealed class UnixPtyConsoleHost : IInstanceConsoleHost
{
    private readonly SafeFileHandle _masterRead;
    private readonly SafeFileHandle? _masterWriteHandle;
    private readonly FileStream _readStream;
    private readonly FileStream _writeStream;
    private readonly Process _process;
    private readonly object _exitTaskGate = new();
    private Task? _exitTask;
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

    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        Task exitTask;
        lock (_exitTaskGate)
            exitTask = _exitTask ??= Task.Run(WaitForChildExit);
        return exitTask.WaitAsync(cancellationToken);
    }

    public void NotifyProcessExited()
    {
        try
        {
            _writeStream.Dispose();
        }
        catch
        {
        }
    }

    public static UnixPtyConsoleHost Create(ProcessStartInfo startInfo, ushort columns, ushort rows)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsFreeBSD())
            throw new PlatformNotSupportedException("Unix PTY host requires a Unix-like OS.");

        var masterFd = OpenPtyMaster(out var slavePath, columns, rows);
        int pid;
        try
        {
            pid = SpawnOnSlave(startInfo, slavePath, masterFd);
        }
        catch
        {
            _ = close(masterFd);
            throw;
        }

        // The PTY master is O_RDWR; dup so the read pump and the write path never share one
        // FileStream, and therefore never share its lock across a blocking read.
        var writeFd = dup(masterFd);
        if (writeFd < 0)
        {
            var error = Marshal.GetLastPInvokeError();
            _ = close(masterFd);
            throw new InvalidOperationException($"dup(master) failed: {error}");
        }

        var masterRead = new SafeFileHandle((IntPtr)masterFd, ownsHandle: true);
        var masterWrite = new SafeFileHandle((IntPtr)writeFd, ownsHandle: true);
        FileStream? readStream = null;
        FileStream? writeStream = null;
        try
        {
            // isAsync:true throws for PTY FDs on macOS/Linux; the pump runs on a dedicated thread.
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

    /// <summary>
    /// Opens the PTY pair and applies the initial window size, so a child that queries
    /// <c>stty size</c> before the first resize sees the requested geometry rather than 0x0.
    /// </summary>
    private static int OpenPtyMaster(out string slavePath, ushort columns, ushort rows)
    {
        var master = posix_openpt(O_Rdwr | O_NoCtty | O_Cloexec);
        if (master < 0)
            throw new InvalidOperationException($"posix_openpt failed: {Marshal.GetLastPInvokeError()}");

        try
        {
            if (grantpt(master) != 0)
                throw new InvalidOperationException($"grantpt failed: {Marshal.GetLastPInvokeError()}");
            if (unlockpt(master) != 0)
                throw new InvalidOperationException($"unlockpt failed: {Marshal.GetLastPInvokeError()}");

            var buffer = new byte[PathMax];
            if (ptsname_r(master, buffer, buffer.Length) != 0)
                throw new InvalidOperationException($"ptsname_r failed: {Marshal.GetLastPInvokeError()}");

            var length = Array.IndexOf(buffer, (byte)0);
            slavePath = Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length);

            var winsize = new Winsize { Rows = rows, Cols = columns };
            if (ioctl(master, IoTioCswinsz, ref winsize) != 0)
            {
                // A missing window size is not fatal: Resize can still apply one later.
                _ = Marshal.GetLastPInvokeError();
            }

            return master;
        }
        catch
        {
            _ = close(master);
            throw;
        }
    }

    /// <summary>
    /// Spawns the child with the PTY slave on stdin/stdout/stderr and a new session, so the slave
    /// becomes its controlling terminal. All child-side setup happens inside libc.
    /// </summary>
    private static int SpawnOnSlave(ProcessStartInfo startInfo, string slavePath, int masterFd)
    {
        var fileActions = AllocSpawnBuffer();
        var attributes = AllocSpawnBuffer();
        var argv = IntPtr.Zero;
        var envp = IntPtr.Zero;
        var fileActionsInitialized = false;
        var attributesInitialized = false;
        try
        {
            Check(posix_spawn_file_actions_init(fileActions), "posix_spawn_file_actions_init");
            fileActionsInitialized = true;
            Check(posix_spawnattr_init(attributes), "posix_spawnattr_init");
            attributesInitialized = true;

            // A new session plus the slave as stdin makes the slave the controlling terminal.
            Check(
                posix_spawnattr_setflags(attributes, (short)PosixSpawnSetsid),
                "posix_spawnattr_setflags");

            foreach (var fd in new[] { 0, 1, 2 })
            {
                Check(
                    posix_spawn_file_actions_addopen(fileActions, fd, slavePath, O_Rdwr, 0),
                    "posix_spawn_file_actions_addopen");
            }

            // The child must not inherit the master, or the PTY never reports EOF when it exits.
            Check(
                posix_spawn_file_actions_addclose(fileActions, masterFd),
                "posix_spawn_file_actions_addclose");

            if (!string.IsNullOrEmpty(startInfo.WorkingDirectory))
            {
                Check(
                    posix_spawn_file_actions_addchdir_np(fileActions, startInfo.WorkingDirectory),
                    "posix_spawn_file_actions_addchdir_np");
            }

            var childEnvironment = BuildChildEnvironment(startInfo, slavePath);
            argv = AllocVector(BuildArgv(startInfo).Where(argument => argument is not null).Select(argument => argument!));
            envp = AllocVector(childEnvironment);

            var result = posix_spawn(
                out var pid,
                startInfo.FileName,
                fileActions,
                attributes,
                argv,
                envp);

            if (result != 0)
            {
                throw new InvalidOperationException(
                    $"posix_spawn failed for '{startInfo.FileName}': {result} ({Marshal.GetPInvokeErrorMessage(result)})");
            }

            return pid;
        }
        finally
        {
            FreeVector(argv);
            FreeVector(envp);
            if (attributesInitialized)
                _ = posix_spawnattr_destroy(attributes);
            if (fileActionsInitialized)
                _ = posix_spawn_file_actions_destroy(fileActions);
            Marshal.FreeHGlobal(fileActions);
            Marshal.FreeHGlobal(attributes);
        }
    }

    /// <summary>
    /// The child inherits this process's environment plus the PTY-specific values, matching what the
    /// previous fork-based child produced.
    /// </summary>
    private static string[] BuildChildEnvironment(ProcessStartInfo startInfo, string slavePath)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
                values[key] = value;
        }

        foreach (var pair in startInfo.Environment)
        {
            if (pair.Value is not null)
                values[pair.Key] = pair.Value;
        }

        if (!values.TryGetValue("TERM", out var term) || string.IsNullOrWhiteSpace(term))
            values["TERM"] = "xterm-256color";

        // Prefer truecolor when log colorizers / jline check COLORTERM.
        if (!values.TryGetValue("COLORTERM", out var colorTerm) || string.IsNullOrWhiteSpace(colorTerm))
            values["COLORTERM"] = "truecolor";

        values["COLUMNS"] = startInfo.Environment.TryGetValue("COLUMNS", out var columns) && !string.IsNullOrWhiteSpace(columns)
            ? columns
            : values.GetValueOrDefault("COLUMNS", "120");
        values["LINES"] = startInfo.Environment.TryGetValue("LINES", out var lines) && !string.IsNullOrWhiteSpace(lines)
            ? lines
            : values.GetValueOrDefault("LINES", "40");

        // Shells and tools use this to detect a terminal; keep it accurate for the slave we opened.
        values["TTY"] = slavePath;

        return [.. values.Select(pair => $"{pair.Key}={pair.Value}")];
    }

    private static void Check(int result, string operation)
    {
        if (result != 0)
        {
            throw new InvalidOperationException(
                $"{operation} failed: {result} ({Marshal.GetPInvokeErrorMessage(result)})");
        }
    }

    private static IntPtr AllocSpawnBuffer()
    {
        // posix_spawn_file_actions_t and posix_spawnattr_t are opaque; glibc and musl both fit in
        // well under this, and an oversized zeroed buffer is harmless.
        const int size = 512;
        var buffer = Marshal.AllocHGlobal(size);
        Marshal.Copy(new byte[size], 0, buffer, size);
        return buffer;
    }

    private static IntPtr AllocVector(IEnumerable<string> values)
    {
        var items = values.ToArray();
        var vector = Marshal.AllocHGlobal(IntPtr.Size * (items.Length + 1));
        for (var i = 0; i < items.Length; i++)
            Marshal.WriteIntPtr(vector, i * IntPtr.Size, Marshal.StringToCoTaskMemUTF8(items[i]));

        Marshal.WriteIntPtr(vector, items.Length * IntPtr.Size, IntPtr.Zero);
        return vector;
    }

    private static void FreeVector(IntPtr vector)
    {
        if (vector == IntPtr.Zero)
            return;

        for (var offset = 0; ; offset += IntPtr.Size)
        {
            var item = Marshal.ReadIntPtr(vector, offset);
            if (item == IntPtr.Zero)
                break;
            Marshal.FreeCoTaskMem(item);
        }

        Marshal.FreeHGlobal(vector);
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

    private const int O_Rdwr = 2;
    private const int O_NoCtty = 0x100;
    private const int O_Cloexec = 0x80000;
    private const int PathMax = 4096;
    private const int PosixSpawnSetsid = 0x80;

    [StructLayout(LayoutKind.Sequential)]
    private struct Winsize
    {
        public ushort Rows;
        public ushort Cols;
        public ushort XPixel;
        public ushort YPixel;
    }

    private void WaitForChildExit()
    {
        while (true)
        {
            var result = waitpid(_process.Id, out _, 0);
            if (result == _process.Id)
                return;

            if (result != -1)
                continue;

            var error = Marshal.GetLastPInvokeError();
            if (error == 4) // EINTR
                continue;
            if (error == 10) // ECHILD: already reaped by another waiter.
                return;

            throw new Win32Exception(error);
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_openpt(int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int grantpt(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int unlockpt(int fd);

    [DllImport("libc", EntryPoint = "ptsname_r", SetLastError = true)]
    private static extern int ptsname_r(int fd, byte[] buffer, nint bufferLength);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref Winsize arg);

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawn_file_actions_init(IntPtr fileActions);

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawn_file_actions_destroy(IntPtr fileActions);

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawn_file_actions_addopen(
        IntPtr fileActions,
        int fd,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        int mode);

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawn_file_actions_addclose(IntPtr fileActions, int fd);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addchdir_np", SetLastError = true)]
    private static extern int posix_spawn_file_actions_addchdir_np(
        IntPtr fileActions,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawnattr_init(IntPtr attributes);

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawnattr_destroy(IntPtr attributes);

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawnattr_setflags(IntPtr attributes, short flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawn(
        out int pid,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        IntPtr fileActions,
        IntPtr attributes,
        IntPtr argv,
        IntPtr envp);

    [DllImport("libc", SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);

    [DllImport("libc", SetLastError = true)]
    private static extern int dup(int oldfd);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}
