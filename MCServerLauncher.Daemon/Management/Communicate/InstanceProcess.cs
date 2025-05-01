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

    public InstanceProcess(ProcessStartInfo info, bool isMcServer, int monitorFrequency = 2000)
    {
        var process = new Process
        {
            StartInfo = info,
            EnableRaisingEvents = true
        };
        _process = process;

        process.OutputDataReceived += (_, arg) =>
        {
            if (arg.Data is not null) OnLog?.Invoke(arg.Data);
        };

        process.ErrorDataReceived += (_, arg) =>
        {
            if (arg.Data is not null) OnLog?.Invoke("[STDERR] " + arg.Data);
        };

        Monitor = new ProcessMonitor(this, isMcServer, monitorFrequency);
    }

    public InstanceStatus Status { get; private set; } = InstanceStatus.Stopped;
    public int ServerProcessId { get; private set; } = -1;
    public bool HasExit => _process.HasExited;
    public ProcessMonitor Monitor { get; }

    public event Action<InstanceStatus>? OnStatusChanged;
    public event Action? OnProcessStarted;

    public event Action<string>? OnLog;

    public async Task<bool> StartAsync(int delayToCheck = 500)
    {
        ChangeStatus(InstanceStatus.Starting);

        var fileName = _process.StartInfo.FileName;
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        await Task.Delay(delayToCheck);

        if (!_process.HasExited)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                ServerProcessId = await Task.Run(() => ProcessTreeHelper.FindSubProcessPid(_process.Id, fileName));
            else
                ServerProcessId = _process.Id;
        }

        if (!_process.HasExited) OnProcessStarted?.Invoke();

        return !_process.HasExited;
    }

    public Task WaitForExitAsync(CancellationToken ct = default)
    {
        return _process.WaitForExitAsync(ct);
    }

    public void Close()
    {
        _process.Close();
    }

    public void KillProcess()
    {
        _process.Kill();
        _process.WaitForExit();
        ChangeStatus(InstanceStatus.Stopped);
    }

    public void WriteLine(string? message)
    {
        if (message is null) return;
        _process.StandardInput.WriteLine(message);
    }


    private void ChangeStatus(InstanceStatus newStatus)
    {
        Status = newStatus;
        OnStatusChanged?.Invoke(newStatus);
    }

    protected override void ProtectedDispose()
    {
        _process.Dispose();
    }

    public class ProcessMonitor
    {
        public static readonly Regex DonePattern = new("Done \\(\\d+\\.\\d{1,3}s\\)! For help, type [\"']help[\"']$",
            RegexOptions.Compiled);

        private readonly IAsyncLazyCell<(long Memory, double Cpu)> _monitor;

        public ProcessMonitor(InstanceProcess process, bool isMcServer, int freq = 2000)
        {
            _monitor = new AsyncTimedLazyCell<(long Memory, double Cpu)>(() =>
            {
                if (process.Status is InstanceStatus.Running or InstanceStatus.Starting)
                    return ProcessInfo.GetProcessUsageAsync(process.ServerProcessId);

                return Task.FromResult((0L, 0.0));
            }, TimeSpan.FromMilliseconds(freq));

            if (isMcServer)
                // 如果是mc服务器, 那么监听输出, 更具输出内容判断状态
                process._process.OutputDataReceived += (_, args) =>
                {
                    var msg = args.Data;

                    if (msg is null) return;

                    if (DonePattern.IsMatch(msg.TrimEnd()))
                        process.ChangeStatus(InstanceStatus.Running);
                    else if (msg.Contains("Stopping the server"))
                        process.ChangeStatus(InstanceStatus.Stopping);
                    else if (msg.Contains("Minecraft has crashed")) process.ChangeStatus(InstanceStatus.Crashed);
                };
            else
                // 如果不是mc服务器, 那么直接将状态置为运行
                process.OnProcessStarted += () => process.ChangeStatus(InstanceStatus.Running);

            process._process.Exited += (_, args) => process.ChangeStatus(InstanceStatus.Stopped);
        }

        public async Task<InstancePerformanceCounter> GetMonitorData()
        {
            var (mem, cpu) = await _monitor.Value;
            return new InstancePerformanceCounter(cpu, mem);
        }
    }
}