using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.Modules;
using Serilog;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace MCServerLauncher.WPF.View.Components.InstanceManager
{
    /// <summary>
    ///    InstanceCard.xaml 的交互逻辑
    /// </summary>
    public partial class InstanceCard : INotifyPropertyChanged
    {
        private Guid _instanceId;
        private string _instanceName;
        private string _instanceType;
        private string _mcVersion;
        private InstanceStatus _status;
        private int _playerCount;
        private double _cpuUsage;
        private long _memoryUsage;
        private bool _isSelected;

        public InstanceCard()
        {
            InitializeComponent();
            DataContext = this;
        }

        public string daemonAddr { get; set; }

        /// <summary>
        /// Daemon configuration for this instance
        /// </summary>
        public Constants.DaemonConfigModel DaemonConfig { get; set; }

        /// <summary>
        /// Selection changed event for batch operations
        /// </summary>
        public event EventHandler<bool>? SelectionChanged;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    SelectCheckBox.IsChecked = value;
                    OnPropertyChanged();
                }
            }
        }

        public Guid InstanceId
        {
            get => _instanceId;
            set
            {
                if (_instanceId != value)
                {
                    _instanceId = value;
                    OnPropertyChanged();
                }
            }
        }

        public string InstanceName
        {
            get => _instanceName;
            set
            {
                if (_instanceName != value)
                {
                    _instanceName = value;
                    InstanceNameTextBlock.Text = value;
                    OnPropertyChanged();
                }
            }
        }

        public string InstanceType
        {
            get => _instanceType;
            set
            {
                if (_instanceType != value)
                {
                    _instanceType = value;
                    InstanceTypeTextBlock.Text = value;
                    OnPropertyChanged();
                }
            }
        }

        public string McVersion
        {
            get => _mcVersion;
            set
            {
                if (_mcVersion != value)
                {
                    _mcVersion = value;
                    McVersionTextBlock.Text = string.IsNullOrEmpty(value) ? "N/A" : value;
                    OnPropertyChanged();
                }
            }
        }

        public InstanceStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusText));
                    UpdateStatusBadge();
                    UpdateMenuItemsState();
                }
            }
        }

        public string StatusText => Status.ToString();

        public int PlayerCount
        {
            get => _playerCount;
            set
            {
                if (_playerCount != value)
                {
                    _playerCount = value;
                    PlayerCountTextBlock.Text = value.ToString();
                    OnPropertyChanged();
                }
            }
        }

        public double CpuUsage
        {
            get => _cpuUsage;
            set
            {
                if (Math.Abs(_cpuUsage - value) > 0.01)
                {
                    _cpuUsage = value;
                    OnPropertyChanged();
                }
            }
        }

        public long MemoryUsage
        {
            get => _memoryUsage;
            set
            {
                if (_memoryUsage != value)
                {
                    _memoryUsage = value;
                    OnPropertyChanged();
                }
            }
        }

        private void UpdateStatusBadge()
        {
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = StatusText;

                switch (Status)
                {
                    case InstanceStatus.Running:
                        StatusBadge.Background = new SolidColorBrush(Color.FromRgb(16, 124, 16)); // Green
                        StatusTextBlock.Foreground = Brushes.White;
                        break;
                    case InstanceStatus.Starting:
                        StatusBadge.Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)); // Blue
                        StatusTextBlock.Foreground = Brushes.White;
                        break;
                    case InstanceStatus.Stopping:
                        StatusBadge.Background = new SolidColorBrush(Color.FromRgb(255, 140, 0)); // Orange
                        StatusTextBlock.Foreground = Brushes.White;
                        break;
                    case InstanceStatus.Stopped:
                        StatusBadge.Background = new SolidColorBrush(Color.FromRgb(153, 153, 153)); // Gray
                        StatusTextBlock.Foreground = Brushes.White;
                        break;
                    case InstanceStatus.Crashed:
                        StatusBadge.Background = new SolidColorBrush(Color.FromRgb(232, 17, 35)); // Red
                        StatusTextBlock.Foreground = Brushes.White;
                        break;
                }
            });
        }

        private void UpdateMenuItemsState()
        {
            Dispatcher.Invoke(() =>
            {
                bool isRunning = Status == InstanceStatus.Running || Status == InstanceStatus.Starting;
                bool isStopped = Status == InstanceStatus.Stopped || Status == InstanceStatus.Crashed;

                StartMenuItem.IsEnabled = isStopped;
                StopMenuItem.IsEnabled = isRunning;
                RestartMenuItem.IsEnabled = isRunning;
                KillMenuItem.IsEnabled = isRunning;
            });
        }

        private void SelectCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            _isSelected = SelectCheckBox.IsChecked == true;
            SelectionChanged?.Invoke(this, _isSelected);
        }

        /// <summary>
        /// Open console window for this instance
        /// </summary>
        public void OpenConsole()
        {
            if (DaemonConfig == null)
            {
                Notification.Push(
                    "Error",
                    "Daemon configuration not set for this instance",
                    true,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error
                );
                return;
            }

            Instance.InitializeNewInstanceConsole(DaemonConfig, InstanceId);
        }

        private void ConsoleButton_Click(object sender, RoutedEventArgs e)
        {
            OpenConsole();
        }

        private async void StartInstance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var daemon = await DaemonsWsManager.Get(DaemonConfig);
                if (daemon == null)
                {
                    Notification.Push("Error", "Failed to connect to daemon", true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
                    return;
                }

                await daemon.StartInstanceAsync(InstanceId);
                Notification.Push("Success", $"Starting instance: {InstanceName}", false, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success);
                Log.Information("[InstanceCard] Started instance {0}", InstanceId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceCard] Failed to start instance {0}", InstanceId);
                Notification.Push("Error", $"Failed to start instance: {ex.Message}", true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
            }
            finally
            {

            }
        }

        private async void StopInstance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var daemon = await DaemonsWsManager.Get(DaemonConfig);
                if (daemon == null)
                {
                    Notification.Push("Error", "Failed to connect to daemon", true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
                    return;
                }

                await daemon.StopInstanceAsync(InstanceId);
                Notification.Push("Success", $"Stopping instance: {InstanceName}", false, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success);
                Log.Information("[InstanceCard] Stopped instance {0}", InstanceId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceCard] Failed to stop instance {0}", InstanceId);
                Notification.Push("Error", $"Failed to stop instance: {ex.Message}", true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
            }
        }

        private async void RestartInstance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var daemon = await DaemonsWsManager.Get(DaemonConfig);
                if (daemon == null)
                {
                    Notification.Push("Error", "Failed to connect to daemon", true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
                    return;
                }

                await daemon.RestartInstanceAsync(InstanceId);
                Notification.Push("Success", $"Restarting instance: {InstanceName}", false, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success);
                Log.Information("[InstanceCard] Restarted instance {0}", InstanceId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceCard] Failed to restart instance {0}", InstanceId);
                Notification.Push("Error", $"Failed to restart instance: {ex.Message}", true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
            }
        }

        private async void KillInstance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var daemon = await DaemonsWsManager.Get(DaemonConfig);
                if (daemon == null)
                {
                    Notification.Push("Error", "Failed to connect to daemon", true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
                    return;
                }

                await daemon.KillInstanceAsync(InstanceId);
                Notification.Push("Warning", $"Killed instance: {InstanceName}", false, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Warning);
                Log.Warning("[InstanceCard] Killed instance {0}", InstanceId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceCard] Failed to kill instance {0}", InstanceId);
                Notification.Push("Error", $"Failed to kill instance: {ex.Message}", true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
            }
        }

        private async void DeleteInstance_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new iNKORE.UI.WPF.Modern.Controls.ContentDialog
            {
                Title = "Confirm Deletion",
                Content = $"Are you sure you want to delete instance '{InstanceName}'?\nThis action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = iNKORE.UI.WPF.Modern.Controls.ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != iNKORE.UI.WPF.Modern.Controls.ContentDialogResult.Primary)
                return;

            try
            {
                var daemon = await DaemonsWsManager.Get(DaemonConfig);
                if (daemon == null)
                {
                    Notification.Push("Error", "Failed to connect to daemon", true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
                    return;
                }

                await daemon.RemoveInstanceAsync(InstanceId);
                Notification.Push("Success", $"Deleted instance: {InstanceName}", false, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success);
                Log.Information("[InstanceCard] Deleted instance {0}", InstanceId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceCard] Failed to delete instance {0}", InstanceId);
                Notification.Push("Error", $"Failed to delete instance: {ex.Message}", true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}