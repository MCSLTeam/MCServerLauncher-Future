using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.Modules;
using Serilog;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace MCServerLauncher.WPF.InstanceConsole.View.Components
{
    /// <summary>
    ///    MinecraftInstanceAddress.xaml 的交互逻辑
    /// </summary>
    public partial class MinecraftInstanceAddress : IInstanceBoardComponent
    {
        private bool _isLoading;
        private bool _hasError;

        public MinecraftInstanceAddress()
        {
            InitializeComponent();
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                _isLoading = value;
                Dispatcher.Invoke(() =>
                {
                    ToggleIPButton.IsEnabled = !value;
                });
            }
        }

        public bool HasError
        {
            get => _hasError;
            private set => _hasError = value;
        }

        /// <summary>
        ///    Server IP address.
        /// </summary>
        public string ServerIP
        {
            get => AddressTextBox.Text;
            set
            {
                Dispatcher.Invoke(() =>
                {
                    AddressTextBox.Text = value;
                });
            }
        }

        public async Task InitializeAsync()
        {
            try
            {
                IsLoading = true;
                HasError = false;

                await RefreshAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MinecraftInstanceAddress] Failed to initialize");
                HasError = true;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task RefreshAsync()
        {
            try
            {
                var report = InstanceDataManager.Instance.CurrentReport;
                if (report?.Properties != null)
                {
                    // Try to get server IP from properties
                    if (report.Properties.TryGetValue("server-ip", out var serverIp) && !string.IsNullOrEmpty(serverIp))
                    {
                        ServerIP = serverIp;
                    }
                    else
                    {
                        // Default to local address
                        ServerIP = "127.0.0.1";
                    }

                    // Add port if available
                    if (report.Properties.TryGetValue("server-port", out var serverPort) && !string.IsNullOrEmpty(serverPort))
                    {
                        ServerIP = $"{ServerIP}:{serverPort}";
                    }
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MinecraftInstanceAddress] Failed to refresh");
                HasError = true;
            }
        }

        private void ToggleIP(object sender, RoutedEventArgs e)
        {
            AddressTextBox.Visibility = AddressTextBox.Visibility == Visibility.Hidden ? Visibility.Visible : Visibility.Hidden;
            ToggleIPButton.Content = AddressTextBox.Visibility == Visibility.Hidden ? Lang.Tr["ClickToView"] : Lang.Tr["ClickToHide"];
        }
    }
}