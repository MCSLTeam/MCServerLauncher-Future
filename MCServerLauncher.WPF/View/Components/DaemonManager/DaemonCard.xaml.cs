using iNKORE.UI.WPF.Modern.Common.IconKeys;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using Serilog;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.DaemonClient.Connection;

namespace MCServerLauncher.WPF.View.Components.DaemonManager
{
    /// <summary>
    ///     DaemonCard.xaml 的交互逻辑
    /// </summary>
    public partial class DaemonCard : IDaemonCard
    {
        public DaemonCard()
        {
            InitializeComponent();
            Token = string.Empty;
            EndPoint = string.Empty;
        }

        private string SystemTypeString = string.Empty;
        public bool IsSecure { get; set; }
        public string EndPoint { get; set; }
        public int Port { get; set; }
        public string Token { get; set; }
        public string SystemType
        {
            get => SystemTypeString;
            set
            {
                SystemTypeString = value;
                SystemIcon.Source = SystemTypeString switch
                {
                    "Windows" => FindResource("WindowsDrawingImage") as ImageSource,
                    "Darwin" => FindResource("DarwinDrawingImage") as ImageSource,
                    "Linux" => FindResource("GenericLinuxDrawingImage") as ImageSource,
                    _ => null,
                };
            }
        }
        public string FriendlyName
        {
            get => DeamonFriendlyNameTextBlock.Text;
            set => DeamonFriendlyNameTextBlock.Text = value;
        }
        public string Address
        {
            get => (string)GetValue(AddressProperty);
            set => SetValue(AddressProperty, value);
        }
        #region Address Dependency Property
        public static readonly DependencyProperty AddressProperty =
            DependencyProperty.Register("Address", typeof(string), typeof(DaemonCard),
                new PropertyMetadata("", OnAddressChanged));

        private static void OnAddressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DaemonCard control) return;
            if (e.NewValue is not string address) return;
            control.AddressLine.Text = address;
        }
        #endregion

        public string Status
        {
            get => (string)GetValue(StatusProperty);
            set => SetValue(StatusProperty, value);
        }
        #region Status Dependency Property
        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register("Status", typeof(string), typeof(DaemonCard),
                new PropertyMetadata("", OnStatusChanged));

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DaemonCard control) return;
            if (e.NewValue is not string status) return;
            IconAndText NewStatusLine = status switch
            {
                "err" => new() { Content = Lang.Tr["Status_Error"], Icon = SegoeFluentIcons.Error, IsTabStop = false, VerticalAlignment = VerticalAlignment.Top },
                "ok" => new() { Content = Lang.Tr["Status_OK"], Icon = SegoeFluentIcons.Accept, IsTabStop = false, VerticalAlignment = VerticalAlignment.Top },
                "ing" => new() { Content = Lang.Tr["Connecting"], Icon = SegoeFluentIcons.HangUp, IsTabStop = false, VerticalAlignment = VerticalAlignment.Top },
                _ => throw new NotImplementedException(),
            };
            control.StatusLine.Children.Clear();
            control.StatusLine.Children.Add(NewStatusLine);
            //IconAndText NewConnectionControlLine = status switch
            //{
            //    "err" => new() { Content = Lang.Tr["Retry"], Icon = SegoeFluentIcons.Refresh, IsTabStop = false },
            //    "ok" => new() { Content = Lang.Tr["Disconnect"], Icon = SegoeFluentIcons.DisconnectDrive, IsTabStop = false },
            //    "ing" => new() { Content = Lang.Tr["Retry"], Icon = SegoeFluentIcons.Refresh, IsTabStop = false },
            //    _ => throw new NotImplementedException(),
            //};
            //control.ConnectionControlButton.Content = NewConnectionControlLine;

            //control.ConnectionEditButton.IsEnabled = status != "ing";
            //control.ConnectionControlButton.IsEnabled = status != "ing";
        }
        #endregion

        public async Task<bool> ConnectDaemon()
        {
            Status = "ing";
            try
            {
                var daemon = await DaemonsWsManager.Get(new Constants.DaemonConfigModel
                {
                    EndPoint = EndPoint,
                    Port = Port,
                    Token = Token,
                    IsSecure = IsSecure,
                    FriendlyName = FriendlyName
                });

                if (daemon == null)
                {
                    Status = "err";
                    return false;
                }

                var systemInfo = await daemon.GetSystemInfoAsync();

                var systemName = systemInfo.Os.Name;
                var cpuVendor = systemInfo.Cpu.Vendor;
                if (systemName.Contains("Windows NT")) SystemType = "Windows";
                else if (systemName.Contains("Unix"))
                {
                    if (cpuVendor.Contains("Apple")) SystemType = "Darwin";
                    else SystemType = "Linux";
                }
                
                Status = "ok";
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[Daemon] Error occurred when connecting to daemon({(IsSecure ? "wss" : "ws")}://{EndPoint}:{Port}): {e}");
                Status = "err";
                return false;
            }
        }

        public event Func<Task>? EditRequested;
        public event Func<Task>? DeleteRequested;

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (EditRequested != null)
            {
                await EditRequested.Invoke();
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示确认对话框
            var dialog = new ContentDialog
            {
                Title = Lang.Tr["ConfirmDelete"],
                Content = string.Format(Lang.Tr["ConfirmDeleteDaemonMessage"] ?? "Are you sure you want to delete daemon '{0}'?", FriendlyName),
                PrimaryButtonText = Lang.Tr["Delete"],
                CloseButtonText = Lang.Tr["Cancel"],
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    // 断开连接
                    await DaemonsWsManager.Remove(new Constants.DaemonConfigModel
                    {
                        EndPoint = EndPoint,
                        Port = Port,
                        Token = Token,
                        IsSecure = IsSecure,
                        FriendlyName = FriendlyName
                    });

                    // 触发删除事件，通知父级移除此卡片
                    if (DeleteRequested != null)
                    {
                        await DeleteRequested.Invoke();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[Daemon] Error occurred when deleting daemon: {ex}");
                }
            }
        }

        private void RealtimeLogButton_Click(object sender, RoutedEventArgs e)
        {
            // 实时日志功能暂不实现
            // TODO: 未来实现实时日志查看功能
        }
    }
}
