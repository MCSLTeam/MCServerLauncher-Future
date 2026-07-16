using System.Diagnostics;
using System.Runtime.InteropServices;
using MCServerLauncher.Common.ProtoType.Status;

namespace MCServerLauncher.Daemon.Utils.Status;

public static class SystemInfoHelper
{
    public static async Task<SystemInfo> GetSystemInfo()
    {
        return new SystemInfo(
            GetOsInfo(),
            await CpuInfoHelper.GetCpuInfo(),
            await MemoryInfoHelper.GetMemInfo(),
            GetDiskInfo(),
            GetDiskInfos(),
            Application.AppVersion.ToString()
        );
    }

    public static OsInfo GetOsInfo()
    {
        // Environment.OSVersion 在 Unix 上常为 "Unix …"，导致客户端把 macOS 误判为 Linux。
        // 使用 RuntimeInformation 给出平台友好名称，架构仍取 OSArchitecture。
        var arch = RuntimeInformation.OSArchitecture.ToString();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new OsInfo($"Windows {Environment.OSVersion.Version}", arch);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new OsInfo($"macOS {Environment.OSVersion.Version}", arch);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new OsInfo($"Linux {Environment.OSVersion.Version}", arch);
        return new OsInfo(Environment.OSVersion.ToString(), arch);
    }

    public static DriveInformation GetDiskInfo()
    {
        var location = AppContext.BaseDirectory;

        // 获取根路径并进行跨平台处理
        var rootPath = Path.GetPathRoot(location);

        // 针对Linux/macOS的特殊处理
        if (string.IsNullOrEmpty(rootPath) && Path.IsPathRooted(location))
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                rootPath = Path.DirectorySeparatorChar.ToString();
            else
                throw new InvalidOperationException("无法确定根路径");
        }

        // 路径标准化处理
        var normalizedRoot = rootPath!.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar
        );

        // 确保根路径不为空（Linux根目录特例）
        if (normalizedRoot.Length == 0 && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            normalizedRoot = Path.DirectorySeparatorChar.ToString();

        // 查找匹配的驱动器
        var drive = DriveInfo.GetDrives()
            .Select(d => new
            {
                Info = d,
                NormalizedName = NormalizeDriveName(d.Name)
            })
            .FirstOrDefault(x => IsPathMatch(x.NormalizedName, normalizedRoot))?.Info;

        if (drive == null)
            throw new InvalidOperationException($"找不到驱动器：{rootPath}");

        if (!drive.IsReady)
            throw new IOException($"驱动器未就绪：{rootPath}");

        return new DriveInformation(
            drive.DriveFormat,
            (ulong)drive.TotalSize,
            (ulong)drive.AvailableFreeSpace,
            drive.Name
        );
    }

    public static DriveInformation[] GetDiskInfos()
    {
        // 按 Name+TotalSize 去重，避免 macOS 同一物理盘多挂载点导致客户端求和虚高
        return DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Where(d => d.TotalSize > 0)
            .Where(d => d.DriveType is not DriveType.CDRom and not DriveType.NoRootDirectory and not DriveType.Ram)
            .GroupBy(d => $"{d.Name}\0{d.TotalSize}\0{d.AvailableFreeSpace}")
            .Select(g => g.First())
            .Select(d => new DriveInformation(
                d.DriveFormat,
                (ulong)d.TotalSize,
                (ulong)d.AvailableFreeSpace,
                d.Name))
            .ToArray();
    }

    private static string NormalizeDriveName(string path)
    {
        var normalized = path.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar
        );

        // 处理Linux根目录特例
        if (normalized.Length == 0 && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.DirectorySeparatorChar.ToString();

        return normalized;
    }

    private static bool IsPathMatch(string a, string b)
    {
        return string.Equals(
            a,
            b,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal
        );
    }

    public static async Task<string> RunCommandAsync(string command, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null) throw new InvalidOperationException($"Failed to start process: {command} {arguments}");
        var result = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return result.Trim();
    }
}
