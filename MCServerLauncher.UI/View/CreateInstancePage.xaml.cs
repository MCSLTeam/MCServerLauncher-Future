using MCServerLauncher.UI.View.CreateInstanceProvider;
using System.Windows.Controls;
using Page = System.Windows.Controls.Page;

namespace MCServerLauncher.UI.View
{
    /// <summary>
    /// CreateInstancePage.xaml 的交互逻辑
    /// </summary>

    public partial class CreateInstancePage : Page
    {
        public PreCreateInstance preCreateInstance = new();
        public CreateInstancePage()
        {
            InitializeComponent();
            CurrentCreateInstance.Content = preCreateInstance;
        }
        public UserControl NewMinecraftJavaServerPage()
        {
            return new CreateMinecraftJavaInstanceProvider();
        }
        public UserControl NewMinecraftForgeServerPage()
        {
            return new CreateMinecraftForgeInstanceProvider();
        }
        public UserControl NewMinecraftBedrockServerPage()
        {
            return new CreateMinecraftBedrockInstanceProvider();
        }
        public UserControl NewOtherExecutablePage()
        {
            return new CreateOtherExecutableInstanceProvider();
        }
    }
}
