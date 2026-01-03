using Microsoft.Win32;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using static MCServerLauncher.WPF.App;

namespace MCServerLauncher.WPF.Modules
{
    public class Initializer
    {
#pragma warning disable SYSLIB1054 // 使用 “LibraryImportAttribute” 而不是 “DllImportAttribute” 在编译时生成 P/Invoke 封送代码
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int WriteProfileString(string lpszSection, string lpszKeyName, string lpszString);

        [DllImport("gdi32", CharSet = CharSet.Unicode)]
        private static extern int AddFontResource(string lpFileName);
#pragma warning restore SYSLIB1054 // 使用 “LibraryImportAttribute” 而不是 “DllImportAttribute” 在编译时生成 P/Invoke 封送代码

        /// <summary>
        ///    Initialize program data directory.
        /// </summary>
        private static void InitDataDirectory()
        {
            var dataFolders = new List<string>
            {
                "Data",
                Path.Combine("Data", "Logs"),
                Path.Combine("Data", "Logs", "WPF"),
                Path.Combine("Data", "Configuration"),
                Path.Combine("Data", "Configuration", "MCSL")
            };

            foreach (var dataFolder in dataFolders.Where(dataFolder => !Directory.Exists(dataFolder)))
                Directory.CreateDirectory(dataFolder);
        }

        private static void TryRunAsAdmin()
        {
            if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) 
                return;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath!,
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                var args = Environment.GetCommandLineArgs();
                if (args.Length > 1)
                {
                    startInfo.Arguments = string.Join(" ", args.Skip(1).Select(arg => 
                        arg.Contains(' ') ? $"\"{arg}\"" : arg));
                }

                Process.Start(startInfo);
                Environment.Exit(0);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Log.Warning("[Admin] User cancelled UAC prompt");
            }
            catch (Exception ex)
            {
                Log.Error($"[Admin] Failed to restart as administrator: {ex.Message}");
            }
        }

        /// <summary>
        ///    降级管理员权限，重启应用程序为普通用户权限
        /// </summary>
        private static void TryDegradeAdmin()
        {
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                Log.Information("[Admin] Already running without administrator privileges");
                return;
            }

            try
            {
                Log.Information("[Admin] Attempting to restart without administrator privileges");
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath!,
                    UseShellExecute = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                var args = Environment.GetCommandLineArgs();
                if (args.Length > 1)
                {
                    startInfo.Arguments = string.Join(" ", args.Skip(1).Select(arg => 
                        arg.Contains(' ') ? $"\"{arg}\"" : arg));
                }

                Process.Start(startInfo);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log.Error($"[Admin] Failed to restart without administrator privileges: {ex.Message}");
            }
        }

        private static bool IsWindows11OrHigher()
        {
            var version = Environment.OSVersion.Version;
            return version.Major > 10 || (version.Major == 10 && version.Build >= 22000);
        }

        /// <summary>
        ///    Import cert.
        /// </summary>
        private static void InitCert()
        {
            try
            {
                Log.Information("[Cert] Importing certificate");
                
                if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                {
                    Log.Warning("[Cert] Administrator privileges required, attempting to restart");
                    TryRunAsAdmin();
                    return;
                }
                
                using var certStream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("MCServerLauncher.WPF.Resources.MCSLTeam.cer") ?? throw new FileNotFoundException("Embedded resource not found");
                if (!certStream.CanRead) throw new InvalidOperationException("The stream cannot be read");
                var buffer = new byte[certStream.Length];
                certStream.ReadExactly(buffer);
                var certificate = new X509Certificate2(buffer);
                var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadWrite);
                try
                {
                    store.Remove(certificate);
                }
                catch (CryptographicException)
                {
                }

                store.Add(certificate);
                store.Close();
                Log.Information("[Cert] Certificate successfully imported");
                SettingsManager.SaveSetting("App.IsCertImported", true);
                
                Log.Information("[Cert] Certificate import completed, degrading privileges");
                TryDegradeAdmin();
            }
            catch (Exception ex)
            {
                Log.Error($"[Cert] Failed to import certificate. Reason: {ex.Message}");
            }
        }

        /// <summary>
        ///    Initialize program logger.
        /// </summary>
        private static void InitLogger()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Async(a => a.File("Data/Logs/WPF/WPFLog-.txt", rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"))
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }

        /// <summary>
        ///    Install font.
        /// </summary>
        private static void InitFont()
        {
            var fontFileName = "SegoeIcons.ttf";
            var fontRegistryKey = "Segoe Fluent Icons (TrueType)";
            var fontSysPath = Path.Combine(Environment.GetEnvironmentVariable("WINDIR")!, "fonts", fontFileName);

            if (IsWindows11OrHigher())
            {
                using var fontsKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
                var fontNames = fontsKey!.GetValueNames();
                if (fontNames.Any(fontName => fontName.Equals(fontRegistryKey, StringComparison.OrdinalIgnoreCase)))
                {
                    Log.Information("[Font] Segoe Fluent Icons already installed (Windows 11+)");
                    SettingsManager.SaveSetting("App.IsFontInstalled", true);
                    return;
                }
            }
            else
            {
                Log.Information("[Font] Windows 10 or lower detected, font installation required");
            }

            // 需要安装字体，检查管理员权限
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                Log.Warning("[Font] Administrator privileges required, attempting to restart");
                TryRunAsAdmin();
                return;
            }

            try
            {
                // 如果字体文件已存在，先删除（用于 Windows 10 及以下的强制重装）
                if (File.Exists(fontSysPath))
                {
                    Log.Information("[Font] Removing existing font file for reinstallation");
                    try
                    {
                        File.Delete(fontSysPath);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[Font] Failed to delete existing font file: {ex.Message}");
                    }
                }

                using var fontStream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("MCServerLauncher.WPF.Resources.SegoeIcons.ttf") 
                    ?? throw new FileNotFoundException("Embedded resource not found");
                using var fileStream = File.Create(fontSysPath);
                fontStream.CopyTo(fileStream);
                fileStream.Close();

                _ = AddFontResource(fontSysPath);
                _ = WriteProfileString("fonts", fontRegistryKey, fontFileName);
                
                SettingsManager.SaveSetting("App.IsFontInstalled", true);
                Log.Information("[Font] Font successfully installed");
                
                // 字体安装完成后，降级权限重启
                Log.Information("[Font] Font installation completed, degrading privileges");
                TryDegradeAdmin();
            }
            catch (Exception ex)
            {
                Log.Error($"[Font] Failed to install font: {ex.Message}");
            }
        }

        /// <summary>
        ///    Initialize program.
        /// </summary>
        public static void InitApp()
        {
            InitLogger();
            Log.Information($"[Exe] MCServerLauncher Future v{AppVersion}");
            Log.Information($"[Env] WorkingDir: {Environment.CurrentDirectory}");
            Log.Information($"[Env] OS Version: {Environment.OSVersion.Version} (Build {Environment.OSVersion.Version.Build})");
            InitDataDirectory();
            SettingsManager.InitSettings();
            DaemonsListManager.InitDaemonListConfig();
            
            // 检查是否需要管理员权限操作
            bool needAdminRestart = false;
            if (SettingsManager.Get?.App != null && !SettingsManager.Get.App.IsCertImported) 
            { 
                InitCert(); 
                needAdminRestart = !SettingsManager.Get.App.IsCertImported;
            }
            if (SettingsManager.Get?.App != null && !SettingsManager.Get.App.IsFontInstalled) 
            { 
                InitFont(); 
                needAdminRestart = needAdminRestart || !SettingsManager.Get.App.IsFontInstalled;
            }
            
            // 只有在成功完成操作后才继续
            if (needAdminRestart)
            {
                Log.Warning("[Init] Required operations not completed, application may not function properly");
            }
            
            Lang.Tr.ChangeLanguage(new CultureInfo(SettingsManager.Get?.App?.Language ?? throw new InvalidOperationException()));
            DaemonsWsManager.CreateAllDaemonWsAsync().Wait();
        }
    }
}
