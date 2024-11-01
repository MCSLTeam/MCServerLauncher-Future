using System.Windows;
using System.Windows.Media;

namespace MCServerLauncher.WPF.View.Components.DaemonManager
{
    /// <summary>
    ///     DaemonCard.xaml 的交互逻辑
    /// </summary>
    public partial class DaemonCard
    {
        public DaemonCard()
        {
            InitializeComponent();
        }

        private string systemType;
        public string SystemType
        {
            get => systemType;
            set
            {
                systemType = value;
                SystemIcon.Source = systemType switch
                {
                    "Windows" => (ImageSource)Application.Current.Resources["WindowsDrawingImage"],
                    "Apple" => (ImageSource)Application.Current.Resources["AppleDrawingImage"],
                    "Linux" => (ImageSource)Application.Current.Resources["GenericLinuxDrawingImage"],
                    "SUSE" => (ImageSource)Application.Current.Resources["SUSEDrawingImage"],
                    "Ubuntu" => (ImageSource)Application.Current.Resources["UbuntuDrawingImage"],
                    "Fedora" => (ImageSource)Application.Current.Resources["FedoraDrawingImage"],
                    "CentOS" => (ImageSource)Application.Current.Resources["CentOSDrawingImage"],
                    "Debian" => (ImageSource)Application.Current.Resources["DebianDrawingImage"],
                    _ => null,
                };
            }
        }
        public string FriendlyName
        {
            get => DeamonFriendlyNameTextBlock.Text;
            set => DeamonFriendlyNameTextBlock.Text = value;
        }
    }
}
