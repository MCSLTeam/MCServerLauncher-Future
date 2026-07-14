using iNKORE.UI.WPF.Modern.Common.IconKeys;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using Serilog;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace MCServerLauncher.WPF.View.Components.DaemonManager
{
    /// <summary>
    ///     DaemonSetupCard.xaml 的交互逻辑
    /// </summary>
    public partial class DaemonSetupCard : IDaemonCard
    {
        public DaemonSetupCard()
        {
            InitializeComponent();
            EndPoint = string.Empty;
            Token = string.Empty;
            FriendlyName = string.Empty;
            IsSecure = false;
            Address = string.Empty;
            Status = string.Empty;
        }
        public bool IsSecure { get; set; }
        public string EndPoint { get; set; }
        public int Port { get; set; }
        public string Token { get; set; }
        public string FriendlyName { get; set; }
        public string Address
        {
            get => (string)GetValue(AddressProperty);
            set => SetValue(AddressProperty, value);
        }
        #region Address Dependency Property
        public static readonly DependencyProperty AddressProperty =
            DependencyProperty.Register("Address", typeof(string), typeof(DaemonSetupCard),
                new PropertyMetadata("", OnAddressChanged));

        private static void OnAddressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DaemonSetupCard control) return;
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
            DependencyProperty.Register("Status", typeof(string), typeof(DaemonSetupCard),
                new PropertyMetadata("", OnStatusChanged));

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DaemonSetupCard control) return;
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
            IconAndText NewConnectionControlLine = status switch
            {
                "err" => new() { Content = Lang.Tr["Retry"], Icon = SegoeFluentIcons.Refresh, IsTabStop = false },
                "ok" => new() { Content = Lang.Tr["Disconnect"], Icon = SegoeFluentIcons.DisconnectDrive, IsTabStop = false },
                "ing" => new() { Content = Lang.Tr["Retry"], Icon = SegoeFluentIcons.Refresh, IsTabStop = false },
                _ => throw new NotImplementedException(),
            };
            control.ConnectionControlButton.Content = NewConnectionControlLine;

            control.ConnectionEditButton.IsEnabled = status != "ing";
            control.ConnectionControlButton.IsEnabled = status != "ing";
        }
        #endregion

        public async Task<bool> ConnectDaemon()
        {
            Status = "ing";
            try
            {
                DaemonsListManager.AddDaemon(
                    new Constants.DaemonConfigModel
                    {
                        FriendlyName = FriendlyName,
                        EndPoint = EndPoint,
                        Port = Port,
                        Token = Token,
                        IsSecure = IsSecure
                    }
                );
                var config = new Constants.DaemonConfigModel
                {
                    EndPoint = EndPoint,
                    Port = Port,
                    Token = Token,
                    IsSecure = IsSecure,
                    FriendlyName = FriendlyName
                };
                var result = await DaemonsWsManager.Get(config);
                if (result.IsErr(out var error))
                {
                    Log.Error(
                        "[Daemon] Error occurred when connecting to daemon {Address}: {Code}: {Message}",
                        Address,
                        error!.Code,
                        error.Message);
                    Status = "err";
                    return false;
                }

                Log.Information("[Daemon] Connected: {0}", Address);
                Status = "ok";
                return true;
            }
            catch (Exception e)
            {
                await DaemonsWsManager.Remove(new Constants.DaemonConfigModel
                {
                    EndPoint = EndPoint,
                    Port = Port,
                    Token = Token,
                    IsSecure = IsSecure,
                    FriendlyName = FriendlyName
                });
                Log.Error($"[Daemon] Error occurred when connecting to daemon({(IsSecure ? "wss" : "ws")}://{EndPoint}:{Port}): {e}");
                Status = "err";
                return false;
            }
        }
    }
}
