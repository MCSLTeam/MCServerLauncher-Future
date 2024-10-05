using iNKORE.UI.WPF.Modern.Controls;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.View.Components.Generic
{
    /// <summary>
    ///     NotificationCenterFlyoutContent.xaml 的交互逻辑
    /// </summary>
    public partial class NotificationCenterFlyoutContent
    {
        private static NotificationCenterFlyoutContent instance;

        public static NotificationCenterFlyoutContent Instance
        {
            get
            {
                instance ??= new NotificationCenterFlyoutContent();
                return instance;
            }
        }

        private NotificationCenterFlyoutContent()
        {
            InitializeComponent();
        }
    }
}
