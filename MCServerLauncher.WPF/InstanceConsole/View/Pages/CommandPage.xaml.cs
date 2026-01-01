using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.Modules;
using Serilog;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MCServerLauncher.WPF.InstanceConsole.View.Pages
{
    /// <summary>
    ///    CommandPage.xaml 的交互逻辑
    /// </summary>
    public partial class CommandPage
    {
        private static bool isFullscreen = false;
        private bool _isPageLoaded = false;

        public CommandPage()
        {
            InitializeComponent();
            OnFullscreenButtonContent.Visibility = Visibility.Visible;
            OffFullscreenButtonContent.Visibility = Visibility.Collapsed;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isPageLoaded)
            {
                _isPageLoaded = true;
                // Subscribe to log events
                InstanceDataManager.Instance.LogReceived += OnLogReceived;
                CommandInputTextBox.Focus();
            }
        }

        private void OnLogReceived(object? sender, string logMessage)
        {
            Dispatcher.Invoke(() =>
            {
                ConsoleLogTextBox.AppendText(logMessage + Environment.NewLine);
            });
        }

        private void ConsoleLogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Auto scroll to bottom
            ConsoleLogTextBox.ScrollToEnd();
        }

        private async void SendCommand_Click(object sender, RoutedEventArgs e)
        {
            await SendCommandAsync();
        }

        private async void CommandInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await SendCommandAsync();
            }
        }

        private async Task SendCommandAsync()
        {
            var command = CommandInputTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(command))
                return;

            try
            {
                await InstanceDataManager.Instance.SendCommandAsync(command);
                CommandInputTextBox.Clear();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CommandPage] Failed to send command: {0}", command);
                Notification.Push(
                    "Error",
                    $"Failed to send command: {ex.Message}",
                    true,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error
                );
            }
        }

        private async void StopInstance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await InstanceDataManager.Instance.StopInstanceAsync();
                Notification.Push(
                    "Success",
                    "Stop command sent successfully",
                    false,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success
                );
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CommandPage] Failed to stop instance");
                Notification.Push(
                    "Error",
                    $"Failed to stop instance: {ex.Message}",
                    true,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error
                );
            }
        }

        private async void KillInstance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await InstanceDataManager.Instance.KillInstanceAsync();
                Notification.Push(
                    "Success",
                    "Kill command sent successfully",
                    false,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success
                );
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CommandPage] Failed to kill instance");
                Notification.Push(
                    "Error",
                    $"Failed to kill instance: {ex.Message}",
                    true,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error
                );
            }
        }

        private async void RestartInstance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await InstanceDataManager.Instance.RestartInstanceAsync();
                Notification.Push(
                    "Success",
                    "Instance restarted successfully",
                    false,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success
                );
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CommandPage] Failed to restart instance");
                Notification.Push(
                    "Error",
                    $"Failed to restart instance: {ex.Message}",
                    true,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error
                );
            }
        }

        private void ToggleFullscreen(object sender, RoutedEventArgs e)
        {
            var mainWindow = Window.GetWindow(this) as InstanceConsole.Window;
            if (mainWindow != null)
            {
                if (!isFullscreen)
                {
                    mainWindow.WindowStyle = WindowStyle.None;
                    mainWindow.ResizeMode = ResizeMode.NoResize;
                    mainWindow.WindowState = WindowState.Maximized;
                    mainWindow.Topmost = true;
                    isFullscreen = true;
                    OnFullscreenButtonContent.Visibility = Visibility.Collapsed;
                    OffFullscreenButtonContent.Visibility = Visibility.Visible;
                }
                else
                {
                    mainWindow.WindowStyle = WindowStyle.SingleBorderWindow;
                    mainWindow.ResizeMode = ResizeMode.CanResize;
                    mainWindow.WindowState = WindowState.Normal;
                    mainWindow.Topmost = false;
                    isFullscreen = false;
                    OnFullscreenButtonContent.Visibility = Visibility.Visible;
                    OffFullscreenButtonContent.Visibility = Visibility.Collapsed;
                }
                mainWindow.Show();
            }
        }
    }
}