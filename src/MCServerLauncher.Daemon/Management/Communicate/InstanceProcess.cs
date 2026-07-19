using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Utils;
using MCServerLauncher.Daemon.Utils.LazyCell;
using MCServerLauncher.Daemon.Utils.Status;

namespace MCServerLauncher.Daemon.Management.Communicate;

public class InstanceProcess : DisposableObject
{
    private readonly Process _process;
    private readonly bool _isMcServer;
    private readonly ConcurrentQueue<string> _logHistory = new();
    private readonly CancellationTokenSource _pumpCancellation = new();
    private readonly SemaphoreSlim _statusGate = new(1, 1);
    private Task? _stdoutPumpTask;
    private Task? _stderrPumpTask;
    private Task? _completionTask;
    private int _processStarted;
    private int _runningPublished;
    private int _finalized;

    public InstanceProcess(ProcessStartInfo info, bool isMcServer, int monitorFrequency = 2000)
    {
        _process = new Process
        {
            StartInfo = info,
            EnableRaisingEvents = false
        };
        _isMcServer = isMcServer;
        Monitor = new ProcessMonitor(this, monitorFrequency);
    }

    public InstanceStatus Status { get; private set; } = InstanceStatus.Stopped;
    public int ServerProcessId { get; private set; } = -1;
    public bool HasExit => _process.HasExited;
    public ProcessMonitor Monitor { get; }

    public event Func<InstanceStatus, CancellationToken, Task>? OnStatusChanged;
    public event Func<string, CancellationToken, Task>? OnLog;

    internal Task Completion => _completionTask ?? Task.CompletedTask;

    public async Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var fileName = _process.StartInfo.FileName;
        try
        {
            if (!_process.Start())
                return false;
            Volatile.Write(ref _processStarted, 1);

            _stdoutPumpTask = PumpAsync(_process.StandardOutput, isStandardError: false);
            _stderrPumpTask = PumpAsync(_process.StandardError, isStandardError: true);
            _completionTask = FinalizeProcessAsync(_stdoutPumpTask, _stderrPumpTask);

            await Task.Delay(delayToCheck, ct);
            if (_process.HasExited)
            {
                await _completionTask.WaitAsync(CancellationToken.None);
                return false;
            }

            ServerProcessId = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ProcessTreeHelper.FindSubProcessPid(_process.Id, fileName)
                : _process.Id;
            if (!_isMcServer)
                await PublishRunningAsync(ct);
            return true;
        }
        catch
        {
            await TerminateAndDrainAsync();
            throw;
        }
    }

    public async Task WaitForExitAsync(CancellationToken ct = default)
    {
        var completion = _completionTask;
        if (completion is null)
        {
            await _process.WaitForExitAsync(ct);
            return;
        }

        await completion.WaitAsync(ct);
    }

    public void Close()
    {
        _process.Close();
    }

    public IReadOnlyList<string> GetLogHistory()
    {
        return _logHistory.ToArray();
    }

    public void KillProcess()
    {
        _process.Kill();
        _process.WaitForExit();
    }

    public void WriteLine(string? message)
    {
        if (message is null)
            return;
        _process.StandardInput.WriteLine(message);
    }

    protected override void ProtectedDispose()
    {
        _pumpCancellation.Cancel();
        _process.Dispose();
        _pumpCancellation.Dispose();
        _statusGate.Dispose();
    }

    private async Task PumpAsync(
        StreamReader reader,
        bool isStandardError)
    {
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(CancellationToken.None);
                if (line is null)
                    return;

                var message = isStandardError ? "[STDERR] " + line : line;
                AddLogHistory(message);
                await InvokeAsync(OnLog, message, CancellationToken.None);

                if (!isStandardError && _isMcServer)
                {
                    if (ProcessMonitor.DonePattern.IsMatch(line.TrimEnd()))
                        await PublishRunningAsync(CancellationToken.None);
                    else if (line.Contains("Minecraft has crashed", StringComparison.Ordinal))
                        await PublishStatusAsync(InstanceStatus.Crashed, CancellationToken.None);
                }
            }
        }
        catch (OperationCanceledException) when (_pumpCancellation.IsCancellationRequested)
        {
        }
    }

    private async Task FinalizeProcessAsync(Task stdoutPumpTask, Task stderrPumpTask)
    {
        Exception? pumpFailure = null;
        await _process.WaitForExitAsync(CancellationToken.None);
        try
        {
            await Task.WhenAll(stdoutPumpTask, stderrPumpTask);
        }
        catch (Exception exception)
        {
            pumpFailure = exception;
        }

        await PublishStoppedAsync();
        if (pumpFailure is not null)
            throw pumpFailure;
    }

    private async Task TerminateAndDrainAsync()
    {
        if (Volatile.Read(ref _processStarted) == 0)
            return;

        try
        {
            if (!_process.HasExited)
                _process.Kill();
        }
        catch (InvalidOperationException)
        {
        }

        var completion = _completionTask;
        if (completion is not null)
        {
            try
            {
                await completion.WaitAsync(CancellationToken.None);
            }
            catch
            {
                // The startup failure remains the meaningful error for the caller.
            }
        }
    }

    private void AddLogHistory(string log)
    {
        _logHistory.Enqueue(log);
        const int maxLogHistory = 500;
        while (_logHistory.Count > maxLogHistory)
            _logHistory.TryDequeue(out _);
    }

    private async Task PublishRunningAsync(CancellationToken cancellationToken)
    {
        await _statusGate.WaitAsync(CancellationToken.None);
        try
        {
            if (Volatile.Read(ref _finalized) != 0 || _process.HasExited || Interlocked.Exchange(ref _runningPublished, 1) != 0)
                return;

            await ChangeStatusAsync(InstanceStatus.Running, cancellationToken);
        }
        finally
        {
            _statusGate.Release();
        }
    }

    private async Task PublishStoppedAsync()
    {
        await _statusGate.WaitAsync(CancellationToken.None);
        try
        {
            Volatile.Write(ref _finalized, 1);
            await ChangeStatusAsync(InstanceStatus.Stopped, CancellationToken.None);
        }
        finally
        {
            _statusGate.Release();
        }
    }

    private async Task PublishStatusAsync(InstanceStatus newStatus, CancellationToken cancellationToken)
    {
        await _statusGate.WaitAsync(CancellationToken.None);
        try
        {
            if (Volatile.Read(ref _finalized) != 0)
                return;

            await ChangeStatusAsync(newStatus, cancellationToken);
        }
        finally
        {
            _statusGate.Release();
        }
    }

    private Task ChangeStatusAsync(InstanceStatus newStatus, CancellationToken cancellationToken)
    {
        Status = newStatus;
        return InvokeAsync(OnStatusChanged, newStatus, cancellationToken);
    }

    private static async Task InvokeAsync<T>(
        Func<T, CancellationToken, Task>? handlers,
        T value,
        CancellationToken cancellationToken)
    {
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<Func<T, CancellationToken, Task>>())
            await handler(value, cancellationToken);
    }

    public class ProcessMonitor
    {
        // Vanilla/Paper logs may append ` or "?"` after help; keep the Done(...)! core mandatory.
        public static readonly Regex DonePattern = new(
            @"Done \(\d+\.\d{1,3}s\)! For help, type [""']help[""'](?:\s+or\s+[""']\?[""'])?$",
            RegexOptions.Compiled);

        private readonly IAsyncLazyCell<(long Memory, double Cpu)> _monitor;

        public ProcessMonitor(InstanceProcess process, int freq = 2000)
        {
            _monitor = new AsyncTimedLazyCell<(long Memory, double Cpu)>(() =>
            {
                if (process.Status == InstanceStatus.Running &&
                    process.ServerProcessId != -1 &&
                    !process.HasExit)
                {
                    return ProcessInfo.GetProcessUsageAsync(process.ServerProcessId);
                }

                return Task.FromResult((0L, 0.0));
            }, TimeSpan.FromMilliseconds(freq));
        }

        public async Task<InstancePerformanceCounter> GetMonitorData()
        {
            var (mem, cpu) = await _monitor.Value;
            return new InstancePerformanceCounter(cpu, mem);
        }
    }
}
