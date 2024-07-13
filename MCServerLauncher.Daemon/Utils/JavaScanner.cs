/* ---------------------------------------------------------------------------------------------
   MCServerLauncher Future Java Scanner
   Original Author: LxHTT & AresConnor & Tigercrl
   You can only use this file if you are permitted to do so,
   otherwise you may be prosecuted for violating the law.
   Copyright (c) 2022-2024 MCSLTeam. All rights reserved.
--------------------------------------------------------------------------------------------- */
using Newtonsoft.Json;
using Serilog;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MCServerLauncher.Daemon.Utils
{
    public class JavaScanner
    {
        private readonly List<string> MatchedKeys = new()
        {
            "1.", "bin", "cache", "client", "craft", "data", "download", "eclipse", "mine", "mc", "launch",
            "hotspot", "java", "jdk", "jre", "zulu", "dragonwell", "jvm", "microsoft", "corretto", "sigma",
            "mod", "mojang", "net", "netease", "forge", "liteloader", "fabric", "game", "vanilla", "server",
            "optifine", "oracle", "path", "program", "roaming", "local", "run", "runtime", "software", "daemon",
            "temp", "users", "users", "x64", "x86", "lib", "usr", "env", "ext", "file", "data", "green",
            "我的", "世界", "前置", "原版", "启动", "启动", "国服", "官启", "官方", "客户", "应用", "整合", "组件",
            Environment.UserName, "新建文件夹", "服务", "游戏", "环境", "程序", "网易", "软件", "运行", "高清",
            "badlion", "blc", "lunar", "tlauncher", "soar", "cheatbreaker", "hmcl", "pcl", "bakaxl", "fsm", "vape",
            "jetbrains", "intellij", "idea", "pycharm", "webstorm", "clion", "goland", "rider", "datagrip",
            "rider", "appcode", "phpstorm", "rubymine", "jbr", "android", "mcsm", "msl", "mcsl", "3dmark", "arctime",
        };
        private readonly List<string> ExcludedKeys = new() { "$", "{", "}", "__", "office" };
        public class JavaInfo
        {
            public string Path { get; set; }
            public string Version { get; set; }
            public string Architecture { get; set; }
            public override int GetHashCode()
            {
                return HashCode.Combine(Path, Version);
            }
            public override bool Equals(object Obj)
            {
                return Obj is JavaInfo TargetObj &&
                       Path == TargetObj.Path &&
                       Version == TargetObj.Version;
            }
            public override string ToString()
            {
                return JsonConvert.SerializeObject(this, Formatting.Indented);
            }
        }
        private Process TryStartJava(string Path)
        {
            ProcessStartInfo JavaInfo = new()
            {
                FileName = Path,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process JavaProcess = new() { StartInfo = JavaInfo };
            JavaProcess.Start();
            return JavaProcess;
        }
        private string TryRegexJavaVersion(string JavaOutput)
        {
            var VersionPattern = @"(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:[._](\d+))?(?:-(.+))?";
            var ReMatch = Regex.Match(JavaOutput, VersionPattern);
            if (ReMatch.Success)
            {
                return string.Join(".", ReMatch.Groups.Values.Skip(1).Where(g => g.Success).Select(g => g.Value));
            }
            return "Unknown";
        }
        //public async Task<bool> CheckJavaAvailability(JavaInfo javaInfo)
        //{
        //    if (File.Exists(javaInfo.Path))
        //    {
        //        Process JavaProcess = await TryStartJava(javaInfo.Path);
        //        JavaProcess.WaitForExit();
        //        return TryRegexJavaVersion(JavaProcess.StandardError.ReadToEnd()) == javaInfo.Version;
        //    }
        //    else return false;
        //}
        private bool IsMatchedKey(string Path)
        {
            foreach (string excludedKey in ExcludedKeys)
            { 
                if (Path.Contains(excludedKey))
                {
                    return false;
                }
            }
            foreach (string matchedKey in MatchedKeys)
            {
                if (Path.Contains(matchedKey))
                { 
                    return true;
                }
            }
            return false;
        }
        private bool IsMatchedWindows(string Dir)
        {
            return Dir.EndsWith("java.exe");
        }
        private bool IsMatchedUnix(string Dir)
        {
            return Dir.EndsWith("java");
        }
        private async Task<List<JavaInfo>> StartScan(string Path)
        {
            Func<string, bool> Matcher = Environment.OSVersion.Platform == PlatformID.Win32NT ? IsMatchedWindows : IsMatchedUnix;
            List<Process> JavaProcesses = await SingleScanJob(Path, Matcher);
            List<JavaInfo> javaInfos = new();
            foreach (Process JavaProcess in JavaProcesses)
            {
                JavaProcess.WaitForExit();
                string PossibleJavaOutput = JavaProcess.StandardError.ReadToEnd();
                string PossibleJavaVersion = TryRegexJavaVersion(PossibleJavaOutput);
                if (PossibleJavaVersion != "Unknown")
                {
                    JavaInfo javaInfo = new()
                    {
                        Path = JavaProcess.StartInfo.FileName,
                        Version = PossibleJavaVersion,
                        Architecture = RuntimeInformation.OSArchitecture.ToString()
                    };
                    javaInfos.Add(javaInfo);
                }
            }
            return javaInfos;
        }
        private async Task<List<Process>> SingleScanJob(string WorkingPath, Func<string, bool> Matcher)
        {
            List<Process> JavaProcesses = new();
            if (File.Exists(WorkingPath)) {
                return JavaProcesses; // Skip if it is a file
            }
            try
            {
                foreach (string PossibleFile in Directory.GetFileSystemEntries(WorkingPath))
                {
                    string AbsoluteFilePath = Path.GetFullPath(PossibleFile);
                    if (File.Exists(AbsoluteFilePath))
                    {
                        if (Matcher(Path.GetFileName(PossibleFile)))
                        {
                            Log.Debug($"[JVM] Found possible Java \"{AbsoluteFilePath}\", plan to check it");
                            JavaProcesses.Add(TryStartJava(AbsoluteFilePath));
                        }
                        else { }
                    }
                    else if (IsMatchedKey(Path.GetFileName(PossibleFile).ToLower()))  // Deliver a deeper search
                    {
                        JavaProcesses.AddRange(await SingleScanJob(AbsoluteFilePath, Matcher));
                    }
                    else { }
                }
            }
            catch (UnauthorizedAccessException) {  }
            catch (Exception ex) { Log.Error($"[JVM] A error occured while searching dir \"{WorkingPath}\", Reason: {ex.Message}"); }
            return JavaProcesses;
        }
        public async Task<List<JavaInfo>> ScanJava()
        {
            Log.Information("[JVM] Start scanning available Java");

            List<JavaInfo> PossibleJavaPathList = new();
            for (var i = 65; i <= 90; i++)
            {
                string drive = $"{(char)i}:\\";
                if (Directory.Exists(drive))
                {
                    PossibleJavaPathList.AddRange(await StartScan(drive));
                }
            }
            int cnt = 0;
            foreach (JavaInfo PossibleJavaPath in PossibleJavaPathList)
            {
                Log.Information($"[JVM] Found certain Java at: {PossibleJavaPath.Path} (Version: {PossibleJavaPath.Version})");
                cnt++;
            }
            Console.WriteLine($"Total: {cnt}");
            return PossibleJavaPathList;
        }
    }
}