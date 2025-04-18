using System.Diagnostics;
using System.Runtime.InteropServices;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Utils;
using MCServerLauncher.Daemon.Utils.Cache;
using MCServerLauncher.Daemon.Utils.Status;

namespace MCServerLauncher.Daemon.Minecraft.Server.Communicate;

public class InstanceProcess : DisposableObject
{
    private readonly Process _process;
    private readonly IAsyncCacheable<(long Memory, double Cpu)> _monitor;

    public InstanceProcess(ProcessStartInfo info, int monitorFrequency = 2000)
    {
        var process = new Process
        {
            StartInfo = info,
            EnableRaisingEvents = true
        };
        _process = process;
        _monitor = new AsyncTimedCache<(long Memory, double Cpu)>(() =>
        {
            if (Status is InstanceStatus.Running or InstanceStatus.Starting)
            {
                return ProcessInfo.GetProcessUsageAsync(ServerProcessId);
            }

            return Task.FromResult((-1L, 0.0));
        }, TimeSpan.FromMilliseconds(monitorFrequency));

        process.OutputDataReceived += (_, args) =>
        {
            var msg = args.Data;

            if (msg is null) return;

            if (msg.Contains("Done"))
                ChangeStatus(InstanceStatus.Running);
            else if (msg.Contains("Stopping the server"))
                ChangeStatus(InstanceStatus.Stopping);
            else if (msg.Contains("Minecraft has crashed")) ChangeStatus(InstanceStatus.Crashed);
        };

        process.OutputDataReceived += (_, arg) =>
        {
            if (arg.Data is not null) OnLog?.Invoke(arg.Data);
        };

        process.ErrorDataReceived += (_, arg) =>
        {
            if (arg.Data is not null) OnLog?.Invoke("[STDERR] " + arg.Data);
        };

        process.Exited += (_, _) => ChangeStatus(InstanceStatus.Stopped);
    }

    public InstanceStatus Status { get; private set; } = InstanceStatus.Stopped;
    public int ServerProcessId { get; private set; } = -1;

    public bool HasExit => _process.HasExited;

    public event Action<InstanceStatus>? OnStatusChanged;

    public event Action<string>? OnLog;

    public async Task<bool> StartAsync(int delayToCheck = 500)
    {
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        await Task.Delay(delayToCheck);

        if (!_process.HasExited)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                ServerProcessId = await Task.Run(() => ProcessTreeHelper.FindSubProcessPid(_process.Id, "-jar"));
            else
                ServerProcessId = _process.Id;
        }

        if (!_process.HasExited) ChangeStatus(InstanceStatus.Starting);

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

    public async Task<(long Memory, double Cpu)> GetMonitorData() => await _monitor.Value;
    
    protected override void ProtectedDispose()
    {
        _process.Dispose();
    }

    private void ChangeStatus(InstanceStatus newStatus)
    {
        Status = newStatus;
        OnStatusChanged?.Invoke(newStatus);
    }
}