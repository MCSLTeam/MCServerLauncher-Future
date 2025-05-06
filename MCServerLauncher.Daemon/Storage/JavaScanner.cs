/* ---------------------------------------------------------------------------------------------
   MCServerLauncher Future Java Scanner
   Original Author: LxHTT & AresConnor & Tigercrl
   You can only use this file if you are permitted to do so,
   otherwise you may be prosecuted for violating the law.
   Copyright (c) 2022-2025 MCSLTeam. All rights reserved.
--------------------------------------------------------------------------------------------- */

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MCServerLauncher.Common.ProtoType;
using Serilog;

namespace MCServerLauncher.Daemon.Storage;

public static class JavaScanner
{
    private static readonly Regex VersionRegex =
        new(@"(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:[._](\d+))?(?:-(.+))?", RegexOptions.Compiled);

    private static readonly Func<string, bool> Matcher =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? s => s.Equals("java.exe")
            : s => s.Equals("java");

    private static readonly List<string> MatchedKeywords = new()
    {
        "intellij", "cache", "官启", "vape", "组件", "我的", "liteloader", "运行", "pcl", "bin", "appcode", "untitled folder",
        "content", "microsoft", "program", "lunar", "goland", "download", "corretto", "dragonwell", "客户", "client",
        "新建文件夹", "badlion", "usr", "temp", "ext", "run", "server", "软件", "software", "arctime", "jdk", "phpstorm",
        "eclipse", "rider", "x64", "jbr", "环境", "jre", "env", "jvm", "启动", "未命名文件夹", "sigma", "mojang", "daemon",
        "craft", "oracle", "vanilla", "lib", "file", "msl", "x86", "bakaxl", "高清", "local", "mod", "原版", "webstorm",
        "应用", "hotspot", "fabric", "整合", "net", "mine", "服务", "opt", "home", "idea", "clion", "path", "android",
        "green", "zulu", "官方", "forge", "游戏", "blc", "user", "国服", "pycharm", "3dmark", "data", "roaming", "程序", "java",
        "前置", "soar", "1.", "mc", "世界", "jetbrains", "cheatbreaker", "game", "网易", "launch", "fsm", "root",
        Environment.UserName
    };

    private static readonly List<string> ExcludedKeywords = new() { "$", "{", "}", "__", "office", "volumes" };

    private static bool IsMatchedKey(string directoryName)
    {
        directoryName = directoryName.ToLower();
        return !ExcludedKeywords.Any(directoryName.Contains) && MatchedKeywords.Any(directoryName.Contains);
    }

    private static List<string> SplitEnvPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? path.Split(';') : path.Split(':')).ToList();
    }

    private static async Task<JavaInfo?> GetJavaVersionAsync(string path)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        if (!process.Start()) return null;
        await process.WaitForExitAsync();
        var content = await process.StandardError.ReadToEndAsync();
        var match = VersionRegex.Match(content);
        if (!match.Success) return null;

        return new JavaInfo(path, match.Value, content.Contains("64-Bit") ? "x64" : "x86");
    }

    private static void ScanRoot(
        string workingPath,
        List<Task<JavaInfo?>> tasks,
        bool recursive
    )
    {
        if (File.Exists(workingPath)) return; // Skip if it is a file
        try
        {
            foreach (var directoryEntry in Directory.GetFileSystemEntries(workingPath))
            {
                var absoluteFilePath = Path.GetFullPath(directoryEntry);
                if (File.Exists(absoluteFilePath))
                {
                    if (!Matcher(Path.GetFileName(directoryEntry))) continue;
                    Log.Verbose("[JVM] Found possible Java \"{0}\", planning to check", absoluteFilePath);
                    tasks.Add(GetJavaVersionAsync(absoluteFilePath));
                }
                else if (recursive && IsMatchedKey(Path.GetFileName(directoryEntry).ToLower()))
                {
                    ScanRoot(absoluteFilePath, tasks, recursive);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (Exception ex)
        {
            Log.Debug("[JVM] A error occured while searching dir \"{0}\", Reason: {1}", workingPath, ex.Message);
        }
    }

    /// <summary>
    ///     扫描Java
    /// </summary>
    /// <returns></returns>
    public static async Task<JavaInfo[]> ScanJavaAsync()
    {
        var startTime = DateTime.Now;
        Log.Verbose("[JVM] Start scanning available Java");

        List<Task<JavaInfo?>> tasks = new();

        // Disk
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            for (var i = 65; i <= 90; i++)
            {
                var drive = $"{(char)i}:\\";
                if (Directory.Exists(drive)) ScanRoot(drive, tasks, true);
            }
        else
            ScanRoot("/", tasks, true);


        // PATH
        SplitEnvPath().ForEach(path =>
        {
            if (Directory.Exists(path))
            {
                // 判断之前是否扫描过
                if (path.Split(
                        Path.PathSeparator,
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    ).All(IsMatchedKey)) return;
                ScanRoot(path, tasks, false);
            }
        });

        // JAVA_HOME
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (Directory.Exists(javaHome)) ScanRoot(javaHome, tasks, false);


        var javas = await Task.WhenAll(tasks)
            .ContinueWith(t =>
                t.Result
                    .Where(i => i is not null)
                    .OfType<JavaInfo>()
                    .DistinctBy(i => i.Path)
                    .ToArray()
            );

        foreach (var possibleJavaPath in javas)
            Log.Verbose(
                "[JVM] Found certain Java at: {0} (Version: {1})",
                possibleJavaPath.Path,
                possibleJavaPath.Version
            );

        Log.Verbose("Total: {0}, time elapsed: {1}", javas.Length, DateTime.Now - startTime);
        return javas;
    }

    private static bool IsSymlink(string path)
    {
        return new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
    }
}