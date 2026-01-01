using MCServerLauncher.WPF.InstanceConsole;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MCServerLauncher.WPF.Modules
{
    public class Instance
    {
        /// <summary>
        /// Track opened console windows to prevent duplicates
        /// </summary>
        private static readonly Dictionary<Guid, Window> OpenConsoleWindows = new();

        /// <summary>
        /// Initialize and show a new instance console window
        /// </summary>
        /// <param name="daemonConfig">Daemon configuration</param>
        /// <param name="instanceId">Instance GUID</param>
        public static void InitializeNewInstanceConsole(Constants.DaemonConfigModel daemonConfig, Guid instanceId)
        {
            try
            {
                // Check if console window already exists for this instance
                if (OpenConsoleWindows.TryGetValue(instanceId, out var existingWindow))
                {
                    // Bring existing window to front
                    if (existingWindow.IsLoaded)
                    {
                        existingWindow.Activate();
                        existingWindow.Focus();
                        Log.Information("[Instance] Activated existing console window for instance {0}", instanceId);
                        return;
                    }
                    else
                    {
                        // Remove stale reference
                        OpenConsoleWindows.Remove(instanceId);
                    }
                }

                // Create and initialize new console window
                var consoleWindow = new Window();
                consoleWindow.Initialize(daemonConfig, instanceId);

                // Track window closure to remove from dictionary
                consoleWindow.Closed += (s, e) =>
                {
                    OpenConsoleWindows.Remove(instanceId);
                    Log.Information("[Instance] Console window closed for instance {0}", instanceId);
                };

                // Track window in dictionary
                OpenConsoleWindows[instanceId] = consoleWindow;

                // Show window
                consoleWindow.Show();

                Log.Information("[Instance] Opened console window for instance {0}", instanceId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Instance] Failed to open console window for instance {0}", instanceId);
                Notification.Push(
                    "Error",
                    $"Failed to open instance console: {ex.Message}",
                    true,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error
                );
            }
        }

        /// <summary>
        /// Check if a console window is already open for an instance
        /// </summary>
        public static bool IsConsoleWindowOpen(Guid instanceId)
        {
            return OpenConsoleWindows.ContainsKey(instanceId) && OpenConsoleWindows[instanceId].IsLoaded;
        }

        /// <summary>
        /// Close console window for an instance
        /// </summary>
        public static void CloseConsoleWindow(Guid instanceId)
        {
            if (OpenConsoleWindows.TryGetValue(instanceId, out var window))
            {
                window.Close();
            }
        }

        /// <summary>
        /// Close all open console windows
        /// </summary>
        public static void CloseAllConsoleWindows()
        {
            var windowsToClose = OpenConsoleWindows.Values.ToList();
            foreach (var window in windowsToClose)
            {
                window.Close();
            }
            OpenConsoleWindows.Clear();
        }
    }
}
