using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.View.Components.DaemonManager
{
    /// <summary>
    /// NewDaemonConnectionInput.xaml 的交互逻辑
    /// </summary>
    public partial class NewDaemonConnectionInput : UserControl
    {
        public NewDaemonConnectionInput()
        {
            InitializeComponent();
            WebSocketScheme.Text = SecureWebSocketCheckBox.IsChecked == true ? "wss://" : "ws://";
        }

        private void ToggleWebSocketScheme(object sender, RoutedEventArgs e)
        {
            WebSocketScheme.Text = SecureWebSocketCheckBox.IsChecked == true ? "wss://" : "ws://";
        }
    }
}
