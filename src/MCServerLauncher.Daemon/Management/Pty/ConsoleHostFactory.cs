using System.Diagnostics;
using System.Runtime.InteropServices;
using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Daemon.Management.Pty;

internal static class ConsoleHostFactory
{
    public static (Process Process, IInstanceConsoleHost Host, bool UsesExternalProcessLifecycle) Create(
        ProcessStartInfo startInfo,
        ConsoleMode mode,
        ushort columns = 120,
        ushort rows = 40)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        if (mode != ConsoleMode.Pty)
        {
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = false };
            return (process, new PipeConsoleHost(process), UsesExternalProcessLifecycle: false);
        }

        if (OperatingSystem.IsWindows())
        {
            var host = WindowsConPtyConsoleHost.Start(startInfo, columns, rows);
            return (host.Process, host, UsesExternalProcessLifecycle: true);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            var host = UnixPtyConsoleHost.CreateViaForkPty(startInfo, columns, rows);
            return (host.Process, host, UsesExternalProcessLifecycle: true);
        }

        throw new PlatformNotSupportedException(
            $"PTY console mode is not supported on {RuntimeInformation.OSDescription}.");
    }
}
