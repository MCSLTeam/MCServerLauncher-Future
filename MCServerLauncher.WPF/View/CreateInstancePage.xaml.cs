using System.Windows.Controls;
using MCServerLauncher.WPF.View.CreateInstanceProvider;

namespace MCServerLauncher.WPF.View
{
    /// <summary>
    ///     CreateInstancePage.xaml 的交互逻辑
    /// </summary>
    public partial class CreateInstancePage
    {
        public readonly PreCreateInstance PreCreateInstance = new();

        public CreateInstancePage()
        {
            InitializeComponent();
            CurrentCreateInstance.Content = PreCreateInstance;
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