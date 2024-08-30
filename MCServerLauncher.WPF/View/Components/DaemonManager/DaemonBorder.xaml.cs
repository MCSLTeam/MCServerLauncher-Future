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
            bool realStatus = status switch
            {
                "true" => true,
                "false" => false,
                _ => throw new System.NotImplementedException(),
            };
            control.StatusLine.Content = realStatus ? LanguageManager.Localize["Status_OK"] : LanguageManager.Localize["Status_Error"];
            control.StatusLine.Icon = realStatus ? SegoeFluentIcons.Accept : SegoeFluentIcons.Error;
            control.ConnectionControlLine.Content = realStatus ? LanguageManager.Localize["Disconnect"] : LanguageManager.Localize["Retry"];
            control.ConnectionControlLine.Icon = realStatus ? SegoeFluentIcons.DisconnectDrive : SegoeFluentIcons.Refresh;
        }
    }
}