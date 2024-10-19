namespace MCServerLauncher.WPF.View.Components.Generic
{
    /// <summary>
    ///     NotificationCenterFlyoutContent.xaml 的交互逻辑
    /// </summary>
    public partial class NotificationCenterFlyoutContent
    {
        private static NotificationCenterFlyoutContent? _instance;

        public static NotificationCenterFlyoutContent Instance
        {
            get
            {
                _instance ??= new NotificationCenterFlyoutContent();
                return _instance;
            }
        }

        private NotificationCenterFlyoutContent()
        {
            InitializeComponent();
        }
    }
}
