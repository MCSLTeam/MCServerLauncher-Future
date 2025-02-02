using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.View.Components.DaemonManager
{
    internal class Utils
    {
        public static Task<(ContentDialog, NewDaemonConnectionInput)> ConstructConnectDaemonDialog(string endPoint = "", string port = "", bool isSecure = false, string user = "", string pwd = "", string name = "", bool isRetrying = false)
        {
            NewDaemonConnectionInput newDaemonConnectionInput = new();
            newDaemonConnectionInput.wsEdit.Text = endPoint;
            newDaemonConnectionInput.portEdit.Text = port;
            newDaemonConnectionInput.SecureWebSocketCheckBox.IsChecked = isSecure;
            newDaemonConnectionInput.userEdit.Text = user;
            newDaemonConnectionInput.pwdEdit.Password = pwd;
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
    }
}
