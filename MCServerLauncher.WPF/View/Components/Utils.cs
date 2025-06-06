﻿using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Components.CreateInstance;
using MCServerLauncher.WPF.View.Components.DaemonManager;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.View.Components
{
    internal class Utils
    {
        public static Task<(ContentDialog, NewDaemonConnectionInput)> ConstructConnectDaemonDialog(string endPoint = "", string port = "", bool isSecure = false, string token = "", string name = "", bool isRetrying = false)
        {
            NewDaemonConnectionInput newDaemonConnectionInput = new();
            newDaemonConnectionInput.wsEdit.Text = endPoint;
            newDaemonConnectionInput.portEdit.Text = port;
            newDaemonConnectionInput.WebSocketScheme.SelectedIndex = isSecure ? 1 : 0;
            newDaemonConnectionInput.tokenEdit.Password = token;
            newDaemonConnectionInput.friendlyNameEdit.Text = name;
            ContentDialog dialog = new()
            {
                Title = LanguageManager.Localize[isRetrying ? "ConnectDaemonFailedTip" : "ConnectDaemon"],
                PrimaryButtonText = LanguageManager.Localize["Connect"],
                SecondaryButtonText = LanguageManager.Localize["Cancel"],
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = newDaemonConnectionInput
            };
            return Task.FromResult<(ContentDialog, NewDaemonConnectionInput)>((dialog, newDaemonConnectionInput));
        }
        public static Task<(ContentDialog, JvmArgHelper)> ConstructJvmArgHelperDialog(string endPoint = "", string port = "", bool isSecure = false, string token = "", string name = "", bool isRetrying = false)
        {
            JvmArgHelper argHelper = new();
            ContentDialog dialog = new()
            {
                Title = LanguageManager.Localize["JvmArgHelper"],
                PrimaryButtonText = LanguageManager.Localize["Insert"],
                SecondaryButtonText = LanguageManager.Localize["Cancel"],
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = argHelper
            };
            return Task.FromResult<(ContentDialog, JvmArgHelper)>((dialog, argHelper));
        }
    }
}
