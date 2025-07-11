using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Components.CreateInstance;
using MCServerLauncher.WPF.View.Components.DaemonManager;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.View.Components
{
    internal class Utils
    {
        private static readonly string _windowsIllegalChars = new string(System.IO.Path.GetInvalidFileNameChars()) + System.IO.Path.GetInvalidPathChars();
        private static readonly List<string> _windowsIllegalCharsFolderName = new List<string> { "aux", "com1", "com2", "prn", "con", "lpt1", "lpt2", "nul" };

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
                Title = Lang.Tr[isRetrying ? "ConnectDaemonFailedTip" : "ConnectDaemon"],
                PrimaryButtonText = Lang.Tr["Connect"],
                SecondaryButtonText = Lang.Tr["Cancel"],
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
                Title = Lang.Tr["JvmArgHelper"],
                PrimaryButtonText = Lang.Tr["Insert"],
                SecondaryButtonText = Lang.Tr["Cancel"],
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = argHelper
            };
            return Task.FromResult<(ContentDialog, JvmArgHelper)>((dialog, argHelper));
        }

        public static bool ValidateString(string str)
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                return false;
            }

            foreach (char c in str)
            {
                if (_windowsIllegalChars.Contains(c))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool ValidateFolderName(string folderName)
        {
            if (!ValidateString(folderName))
            {
                return false;
            }
            if (_windowsIllegalCharsFolderName.Contains(folderName.ToLower()))
            {
                return false;
            }

            return true;
        }
        
        public static Task<bool> ValidateStringAsync(string str)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(str))
                {
                    return false;
                }

                foreach (char c in str)
                {
                    if (_windowsIllegalChars.Contains(c))
                    {
                        return false;
                    }
                }

                return true;
            });
        }

        public static Task<bool> ValidateFolderNameAsync(string folderName)
        {
            return Task.Run(async () =>
            {
                if (!await ValidateStringAsync(folderName))
                {
                    return false;
                }
                if (_windowsIllegalCharsFolderName.Contains(folderName.ToLower()))
                {
                    return false;
                }

                return true;
            });
        }
    }
}
