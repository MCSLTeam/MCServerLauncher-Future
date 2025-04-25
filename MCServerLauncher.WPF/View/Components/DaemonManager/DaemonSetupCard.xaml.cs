﻿using iNKORE.UI.WPF.Modern.Common.IconKeys;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using Serilog;
using System;
using System.Threading.Tasks;
using System.Windows;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.DaemonClient.Connection;

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
            ThisDaemon = null!;
            EndPoint = string.Empty;
            Token = string.Empty;
            FriendlyName = string.Empty;
            IsSecure = false;
            Address = string.Empty;
            Status = string.Empty;
        }
        private IDaemon ThisDaemon { get; set; }
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
        #endregion

        public async Task<bool> ConnectDaemon()
        {
            Status = "ing";
            try
            {
                ThisDaemon = await Daemon.OpenAsync(EndPoint, Port, Token, IsSecure, new ClientConnectionConfig
                {
                    MaxFailCount = 3,
                    PendingRequestCapacity = 100,
                    HeartBeatTick = TimeSpan.FromSeconds(5),
                    PingTimeout = 5000
                });
                Log.Information("[Daemon] Connected: {0}", Address);
                await ThisDaemon.PingAsync();
                Status = "ok";
                await ThisDaemon.CloseAsync();
                DaemonsListManager.AddDaemon(
                    new DaemonsListManager.DaemonConfigModel
                    {
                        FriendlyName = FriendlyName,
                        EndPoint = EndPoint,
                        Port = Port,
                        Token = Token,
                        IsSecure = IsSecure
                    }
                );
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