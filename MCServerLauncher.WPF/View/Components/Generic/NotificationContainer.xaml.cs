namespace MCServerLauncher.WPF.View.Components.Generic
{
    /// <summary>
    ///     NotificationContainer.xaml 的交互逻辑
    /// </summary>
    public partial class NotificationContainer
    {
        private static NotificationContainer? _instance;

        public static NotificationContainer Instance
        {
            get
            {
                _instance ??= new NotificationContainer();
                return _instance;
            }
        }

        private NotificationContainer()
        {
            InitializeComponent();
        }
    }
}
