using System.Net.WebSockets;
using System.Threading.Tasks;
using System;
using System.Windows;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Modules.Remote;
using Serilog;
using System.Net.Http;
using iNKORE.UI.WPF.Modern.Controls;

namespace MCServerLauncher.WPF.View.Components.DaemonManager
{
    /// <summary>
    ///     DaemonSetupCard.xaml 的交互逻辑
    /// </summary>
    public partial class DaemonSetupCard
    {
        public DaemonSetupCard()
        {
            InitializeComponent();
        }
        public string Address
        {
            get => (string)GetValue(AddressProperty);
            set => SetValue(AddressProperty, value);
        }
        public static readonly DependencyProperty AddressProperty =
            DependencyProperty.Register("Address", typeof(string), typeof(DaemonSetupCard),
                new PropertyMetadata("", OnAddressChanged));

        private static void OnAddressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DaemonSetupCard control) return;
            if (e.NewValue is not string address) return;
            control.AddressLine.Text = address;
        }

        public string Status
        {
            get => (string)GetValue(StatusProperty);
            set => SetValue(StatusProperty, value);
        }
        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register("Status", typeof(string), typeof(DaemonSetupCard),
                new PropertyMetadata("", OnStatusChanged));

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DaemonSetupCard control) return;
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
            IconAndText NewConnectionControlLine = status switch
            {
                "err" => new() { Content = LanguageManager.Localize["Retry"], Icon = SegoeFluentIcons.Refresh, IsTabStop = false },
                "ok" => new() { Content = LanguageManager.Localize["Disconnect"], Icon = SegoeFluentIcons.DisconnectDrive, IsTabStop = false },
                "ing" => new() { Content = LanguageManager.Localize["Retry"], Icon = SegoeFluentIcons.Refresh, IsTabStop = false },
                _ => throw new NotImplementedException(),
            };
            control.ConnectionControlButton.Content = NewConnectionControlLine;

            control.ConnectionEditButton.IsEnabled = status != "ing";
            control.ConnectionControlButton.IsEnabled = status != "ing";
        }
        public bool IsSecure { get; set; }
        public string EndPoint { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string FriendlyName { get; set; }

        public async Task TryConnectDaemon()
        {
            Status = "ing";
            try
            {
                var token = await Daemon.LoginAsync(
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
                    return;
                }
                var daemon = await Daemon.OpenAsync(EndPoint, Port, token, IsSecure, new ClientConnectionConfig
                {
                    MaxPingPacketLost = 3,
                    PendingRequestCapacity = 100,
                    PingInterval = TimeSpan.FromSeconds(5),
                    PingTimeout = 5000
                });
                Log.Information("[Daemon] Connected: {0}", await daemon.PingAsync());
                await Task.Delay(10000);
                Status = "ok";
                await daemon.CloseAsync();
                DaemonsListManager.AddDaemon(
                    new DaemonsListManager.DaemonConfigModel
                    { 
                        FriendlyName = FriendlyName,
                        EndPoint = EndPoint,
                        Port = Port,
                        Username = Username,
                        Password = Password,
                        IsSecure = IsSecure
                    }
                );
            }
            catch (Exception e)
            {
                Log.Error($"[Daemon] Error occurred when connecting to daemon({(IsSecure ? "wss" : "ws")}://{EndPoint}:{Port}): {e}");
                Status = "err";
            }
        }
    }
}