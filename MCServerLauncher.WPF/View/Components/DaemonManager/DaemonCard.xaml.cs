using iNKORE.UI.WPF.Modern.Common.IconKeys;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Modules.Remote;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

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
            token = string.Empty;
            ThisDaemon = null!;
            EndPoint = string.Empty;
            Username = string.Empty;
            Password = string.Empty;
        }

        private string token;
        private IDaemon ThisDaemon { get; set; }

        private string SystemTypeString = string.Empty;
        public bool IsSecure { get; set; }
        public string EndPoint { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
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
                "err" => new() { Content = LanguageManager.Localize["Status_Error"], Icon = SegoeFluentIcons.Error, IsTabStop = false, VerticalAlignment = VerticalAlignment.Top },
                "ok" => new() { Content = LanguageManager.Localize["Status_OK"], Icon = SegoeFluentIcons.Accept, IsTabStop = false, VerticalAlignment = VerticalAlignment.Top },
                "ing" => new() { Content = LanguageManager.Localize["Connecting"], Icon = SegoeFluentIcons.HangUp, IsTabStop = false, VerticalAlignment = VerticalAlignment.Top },
                _ => throw new NotImplementedException(),
            };
            control.StatusLine.Children.Clear();
            control.StatusLine.Children.Add(NewStatusLine);
            //IconAndText NewConnectionControlLine = status switch
            //{
            //    "err" => new() { Content = LanguageManager.Localize["Retry"], Icon = SegoeFluentIcons.Refresh, IsTabStop = false },
            //    "ok" => new() { Content = LanguageManager.Localize["Disconnect"], Icon = SegoeFluentIcons.DisconnectDrive, IsTabStop = false },
            //    "ing" => new() { Content = LanguageManager.Localize["Retry"], Icon = SegoeFluentIcons.Refresh, IsTabStop = false },
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
                token = await Daemon.LoginAsync(
                    address: EndPoint,
                    port: Port,
                    usr: Username,
                    pwd: Password,
                    isSecure: IsSecure,
                    86400
                ) ?? "token not found";
                if (token == "token not found")
                {
                    Status = "err";
                    return false;
                }
                ThisDaemon = await Daemon.OpenAsync(EndPoint, Port, token, IsSecure, new ClientConnectionConfig
                {
                    MaxPingPacketLost = 3,
                    PendingRequestCapacity = 100,
                    PingInterval = TimeSpan.FromSeconds(5),
                    PingTimeout = 5000
                });
                Log.Information("[Daemon] Connected: {0}", Address);
                JToken systemInfo = (await ThisDaemon.GetSystemInfoAsync()).SelectToken("info");
                if (systemInfo is not null)
                {
                    var SystemName = systemInfo.SelectToken("os.name")!.ToString();
                    var CpuVendor = systemInfo.SelectToken("cpu.vendor")!.ToString();
                    if (SystemName.Contains("Windows NT")) SystemType = "Windows";
                    else if (SystemName.Contains("Unix"))
                    {
                        if (CpuVendor.Contains("Apple")) SystemType = "Darwin";
                        else SystemType = "Linux";
                    }
                }
                Status = "ok";
                await ThisDaemon.CloseAsync();
                return true;
            }
            catch (Exception e)
            {
                try { await ThisDaemon.CloseAsync(); } catch { }
                Log.Error($"[Daemon] Error occurred when connecting to daemon({(IsSecure ? "wss" : "ws")}://{EndPoint}:{Port}): {e}");
                Status = "err";
                return false;
            }
        }
    }
}
