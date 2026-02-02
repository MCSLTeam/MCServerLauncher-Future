using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Components;
using MCServerLauncher.WPF.View.Components.DaemonManager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace MCServerLauncher.WPF.View.Pages
{
    /// <summary>
    ///    DaemonManagerPage.xaml 的交互逻辑
    /// </summary>
    public partial class DaemonManagerPage
    {
        public DaemonManagerPage()
        {
            InitializeComponent();
            // Refresh trigger when page is visible
            IsVisibleChanged += async (s, e) =>
            {
                if (IsVisible)
                {
                    await RefreshDaemonListAsync();
                }
            };
        }

        private async Task RefreshDaemonListAsync()
        {
            DaemonCardContainer.Items.Clear();
#pragma warning disable CS8602 // 解引用可能出现空引用。
            if (DaemonsListManager.Get?.Count > 0)
            {
                var connectionTasks = new List<Task>();
                foreach (Constants.DaemonConfigModel daemon in DaemonsListManager.Get)
                {
#pragma warning disable CS8601 // 引用类型赋值可能为 null。
                    DaemonCard daemonCard = new()
                    {
                        Address = $"{(daemon.IsSecure ? "wss" : "ws")}://{daemon.EndPoint}:{daemon.Port}",
                        IsSecure = daemon.IsSecure,
                        EndPoint = daemon.EndPoint,
                        Port = daemon.Port,
                        Token = daemon.Token,
                        FriendlyName = daemon.FriendlyName ?? Lang.Tr["Main_DaemonManagerNavMenu"],
                        Status = "ing",
                    };
#pragma warning restore CS8601 // 引用类型赋值可能为 null。
                    
                    // 为编辑按钮添加事件处理
                    daemonCard.EditRequested += async () => await EditDaemonConnectionAsync(daemon, daemonCard);
                    
                    // 为删除按钮添加事件处理
                    daemonCard.DeleteRequested += async () =>
                    {
                        DaemonCardContainer.Items.Remove(daemonCard);
                        DaemonsListManager.RemoveDaemon(daemon);
                    };
                    
                    DaemonCardContainer.Items.Add(daemonCard);
                    connectionTasks.Add(daemonCard.ConnectDaemon());
                }
                await Task.WhenAll(connectionTasks);
            }
#pragma warning restore CS8602 // 解引用可能出现空引用。
        }

        private async void AddDaemonConnection(object sender, RoutedEventArgs e)
        {
            (ContentDialog dialog, NewDaemonConnectionInput newDaemonConnectionInput) = await Utils.ConstructConnectDaemonDialog();
            dialog.PrimaryButtonClick += (o, args) => TryConnectDaemon(
                endPoint: newDaemonConnectionInput.wsEdit.Text,
                port: newDaemonConnectionInput.portEdit.Text,
                isSecure: newDaemonConnectionInput.WebSocketScheme.SelectionBoxItem.ToString() == "wss://",
                token: newDaemonConnectionInput.tokenEdit.Password,
                friendlyName: newDaemonConnectionInput.friendlyNameEdit.Text
            );
            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private async Task EditDaemonConnectionAsync(Constants.DaemonConfigModel originalConfig, DaemonCard daemonCard)
        {
            (ContentDialog dialog, NewDaemonConnectionInput newDaemonConnectionInput) = await Utils.ConstructConnectDaemonDialog(
                originalConfig.EndPoint,
                originalConfig.Port.ToString(),
                originalConfig.IsSecure,
                originalConfig.Token,
                originalConfig.FriendlyName,
                isRetrying: false,
                isEditing: true
            );

            dialog.PrimaryButtonClick += async (o, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    // 验证输入
                    if (!int.TryParse(newDaemonConnectionInput.portEdit.Text, out int newPort))
                    {
                        args.Cancel = true;
                        return;
                    }

                    string newEndPoint = newDaemonConnectionInput.wsEdit.Text;
                    string newToken = newDaemonConnectionInput.tokenEdit.Password;
                    bool newIsSecure = newDaemonConnectionInput.WebSocketScheme.SelectionBoxItem.ToString() == "wss://";
                    string newFriendlyName = newDaemonConnectionInput.friendlyNameEdit.Text;

                    if (string.IsNullOrWhiteSpace(newEndPoint) || string.IsNullOrWhiteSpace(newToken))
                    {
                        args.Cancel = true;
                        return;
                    }

                    // 断开旧连接
                    await DaemonsWsManager.Remove(originalConfig);

                    // 从配置列表中移除旧配置
                    DaemonsListManager.RemoveDaemon(originalConfig);

                    // 从 UI 中移除旧卡片
                    DaemonCardContainer.Items.Remove(daemonCard);

                    // 创建新配置
                    var newConfig = new Constants.DaemonConfigModel
                    {
                        FriendlyName = newFriendlyName,
                        EndPoint = newEndPoint,
                        Port = newPort,
                        Token = newToken,
                        IsSecure = newIsSecure
                    };

                    // 尝试连接新配置
                    DaemonCard newDaemonCard = new()
                    {
                        EndPoint = newEndPoint,
                        Port = newPort,
                        IsSecure = newIsSecure,
                        Token = newToken,
                        Status = "ing",
                        FriendlyName = newFriendlyName,
                        Address = $"{(newIsSecure ? "wss" : "ws")}://{newEndPoint}:{newPort}"
                    };

                    newDaemonCard.EditRequested += async () => await EditDaemonConnectionAsync(newConfig, newDaemonCard);
                    newDaemonCard.DeleteRequested += async () =>
                    {
                        DaemonCardContainer.Items.Remove(newDaemonCard);
                        DaemonsListManager.RemoveDaemon(newConfig);
                    };

                    DaemonCardContainer.Items.Add(newDaemonCard);

                    if (await newDaemonCard.ConnectDaemon())
                    {
                        // 连接成功，添加到配置列表
                        DaemonsListManager.AddDaemon(newConfig);
                    }
                    else
                    {
                        // 连接失败，移除卡片并重新打开编辑对话框
                        DaemonCardContainer.Items.Remove(newDaemonCard);
                        args.Cancel = true;
                        await EditDaemonConnectionAsync(newConfig, newDaemonCard);
                    }
                }
                finally
                {
                    deferral.Complete();
                }
            };

            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private async void TryConnectDaemon(string endPoint, string port, string token, bool isSecure, string friendlyName)
        {
            try
            {
                int IntPort = int.Parse(port);
            }
            catch (FormatException)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(endPoint) || string.IsNullOrWhiteSpace(port) || string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            var config = new Constants.DaemonConfigModel
            {
                FriendlyName = friendlyName,
                EndPoint = endPoint,
                Port = int.Parse(port),
                Token = token,
                IsSecure = isSecure
            };

            DaemonCard daemon = new()
            {
                EndPoint = endPoint,
                Port = int.Parse(port),
                IsSecure = isSecure,
                Token = token,
                Status = "ing",
                FriendlyName = friendlyName,
                Address = $"{(isSecure ? "wss" : "ws")}://{endPoint}:{int.Parse(port)}"
            };

            daemon.EditRequested += async () => await EditDaemonConnectionAsync(config, daemon);
            daemon.DeleteRequested += async () =>
            {
                DaemonCardContainer.Items.Remove(daemon);
                DaemonsListManager.RemoveDaemon(config);
            };

            DaemonCardContainer.Items.Add(daemon);

            if (await daemon.ConnectDaemon())
            {
                DaemonsListManager.AddDaemon(config);
            }
            else
            {
                DaemonCardContainer.Items.Remove(daemon);
                await EditDaemonConnectionAsync(config, daemon);
            }
        }
    }
}