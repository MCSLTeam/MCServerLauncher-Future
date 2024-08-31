using iNKORE.UI.WPF.Modern.Common.IconKeys;
using MCServerLauncher.WPF.Modules;
using System.Windows;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.View.Components.DaemonManager
{
    /// <summary>
    /// DaemonBorder.xaml 的交互逻辑
    /// </summary>
    public partial class DaemonBorder : UserControl
    {
        public DaemonBorder()
        {
            InitializeComponent();
        }
        public string Address
        {
            get => (string)GetValue(AddressProperty);
            set => SetValue(AddressProperty, value);
        }
        public static readonly DependencyProperty AddressProperty =
            DependencyProperty.Register("Address", typeof(string), typeof(DaemonBorder),
                new PropertyMetadata("", OnAddressChanged));

        private static void OnAddressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DaemonBorder control) return;
            if (e.NewValue is not string address) return;
            control.AddressLine.Text = address;
        }

        public string Status
        {
            get => (string)GetValue(StatusProperty);
            set => SetValue(StatusProperty, value);
        }
        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register("Status", typeof(string), typeof(DaemonBorder),
                new PropertyMetadata("", OnStatusChanged));

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DaemonBorder control) return;
            if (e.NewValue is not string status) return;
            control.StatusLine.Content = status switch
            {
                "ok" => LanguageManager.Localize["Status_OK"],
                "err" => LanguageManager.Localize["Status_Error"],
                "ing" => LanguageManager.Localize["Connecting"],
                _ => throw new System.NotImplementedException(),
            };
            control.StatusLine.Icon = status switch
            {
                "ok" => SegoeFluentIcons.Accept,
                "err" => SegoeFluentIcons.Error,
                "ing" => SegoeFluentIcons.HangUp,
                _ => throw new System.NotImplementedException(),
            }; 
            control.ConnectionControlLine.Content = status switch
            {
                "ok" => LanguageManager.Localize["Disconnect"],
                "err" => LanguageManager.Localize["Retry"],
                "ing" => LanguageManager.Localize["Retry"],
                _ => throw new System.NotImplementedException(),
            };
            //control.ConnectionEditButton.IsEnabled = status != "ing";
            control.ConnectionControlButton.IsEnabled = status != "ing";
            control.ConnectionControlLine.Icon = status switch
            {
                "ok" => SegoeFluentIcons.DisconnectDrive,
                "err" => SegoeFluentIcons.Refresh,
                "ing" => SegoeFluentIcons.Refresh,
                _ => throw new System.NotImplementedException(),
            };
        }

        public string JWT { get; set; }
    }
}