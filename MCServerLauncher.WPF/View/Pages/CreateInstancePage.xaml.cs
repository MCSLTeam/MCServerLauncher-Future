using System.Windows.Controls;
using MCServerLauncher.WPF.View.CreateInstanceProvider;

namespace MCServerLauncher.WPF.View.Pages
{
    /// <summary>
    ///    CreateInstancePage.xaml 的交互逻辑
    /// </summary>
    public partial class CreateInstancePage
    {
        public readonly PreCreateInstance PreCreateInstance = new();

        public CreateInstancePage()
        {
            InitializeComponent();
            CurrentCreateInstance.Content = PreCreateInstance;
        }

        /// <summary>
        ///    Spawn a new Minecraft Java instance creation page.
        /// </summary>
        /// <returns>The user control.</returns>
        public UserControl NewMinecraftJavaServerPage()
        {
            return new CreateMinecraftJavaInstanceProvider();
        }

        /// <summary>
        ///    Spawn a new Minecraft Forge instance creation page.
        /// </summary>
        /// <returns>The user control.</returns>
        public UserControl NewMinecraftForgeServerPage()
        {
            return new CreateMinecraftForgeInstanceProvider();
        }

        /// <summary>
        ///    Spawn a new Minecraft NeoForge instance creation page.
        /// </summary>
        /// <returns>The user control.</returns>
        public UserControl NewMinecraftNeoForgeServerPage()
        {
            return new CreateMinecraftNeoForgeInstanceProvider();
        }

        /// <summary>
        ///    Spawn a new Minecraft Quilt instance creation page.
        /// </summary>
        /// <returns>The user control.</returns>
        public UserControl NewMinecraftQuiltServerPage()
        {
            return new CreateMinecraftQuiltInstanceProvider();
        }

        /// <summary>
        ///    Spawn a new Minecraft Bedrock instance creation page.
        /// </summary>
        /// <returns>The user control.</returns>
        public UserControl NewMinecraftBedrockServerPage()
        {
            return new CreateMinecraftBedrockInstanceProvider();
        }

        /// <summary>
        ///    Spawn a new other executable instance creation page.
        /// </summary>
        /// <returns>The user control.</returns>
        public UserControl NewOtherExecutablePage()
        {
            return new CreateOtherExecutableInstanceProvider();
        }
    }
}