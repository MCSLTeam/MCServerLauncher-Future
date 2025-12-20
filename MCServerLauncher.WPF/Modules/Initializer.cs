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
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int WriteProfileString(string lpszSection, string lpszKeyName, string lpszString);

        [DllImport("gdi32")]
        private static extern int AddFontResource(string lpFileName);

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
            if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return;
            Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().CodeBase)
            {
                UseShellExecute = true,
                Verb = "runas"
            });
            Environment.Exit(0);
        }

        /// <summary>
        ///    Import cert.
        /// </summary>
        private static void InitCert()
        {
            try
            {
                Log.Information("[Cer] Importing certificate");
                TryRunAsAdmin();
                using var certStream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("MCServerLauncher.WPF.Resources.MCSLTeam.cer");
                if (certStream == null) throw new FileNotFoundException("Embedded resource not found");
                if (!certStream.CanRead) throw new InvalidOperationException("The stream cannot be read");
                var buffer = new byte[certStream.Length];
                certStream.Read(buffer, 0, buffer.Length);
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
                Log.Information("[Cer] Certificate successfully imported");
                SettingsManager.SaveSetting("App.IsCertImported", true);
            }
            catch (Exception ex)
            {
                Log.Error($"[Cer] Failed to import certificate. Reason: {ex.Message}");
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

            TryRunAsAdmin();
            using (var fontsKey =
                   Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts"))
            {
                var fontNames = fontsKey!.GetValueNames();
                if (fontNames.Any(fontName => fontName.Equals(fontRegistryKey, StringComparison.OrdinalIgnoreCase)))
                {
                    SettingsManager.SaveSetting("App.IsFontInstalled", true);
                    return;
                }
            }

            using var fontStream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("MCServerLauncher.WPF.Resources.SegoeIcons.ttf") ?? throw new FileNotFoundException("Embedded resource not found");
            using var fileStream = File.Create(fontSysPath);
            fontStream.CopyTo(fileStream);
            AddFontResource(fontSysPath);
            WriteProfileString("fonts", fontRegistryKey, fontFileName);
        }

        /// <summary>
        ///    Initialize program.
        /// </summary>
        public void InitApp()
        {
            InitLogger();
            Log.Information($"[Exe] MCServerLauncher Future v{AppVersion}");
            Log.Information($"[Env] WorkingDir: {Environment.CurrentDirectory}");
            InitDataDirectory();
            SettingsManager.InitSettings();
            DaemonsListManager.InitDaemonListConfig();
            bool needImport = false;
            if (SettingsManager.Get?.App != null && !SettingsManager.Get.App.IsCertImported) { InitCert(); needImport = true; }
            if (SettingsManager.Get?.App != null && !SettingsManager.Get.App.IsFontInstalled) { InitFont(); needImport = true; }
            if (needImport)
            {
                Process.Start(Assembly.GetExecutingAssembly().Location);
                Environment.Exit(0);
            }
            Lang.Tr.ChangeLanguage(new CultureInfo(SettingsManager.Get?.App?.Language ?? throw new InvalidOperationException()));
            DaemonsWsManager.CreateAllDaemonWsAsync().Wait();
        }
    }
}
