using iNKORE.UI.WPF.Controls;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.CreateInstanceProvider;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

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

        #region Creeate Instance Pages

        public UserControl NewPreMinecraftInstancePage()
        {
            return new PreCreateMinecraftInstance();
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
        ///    Spawn a new Minecraft Fabric instance creation page.
        /// </summary>
        /// <returns>The user control.</returns>
        public UserControl NewMinecraftFabricServerPage()
        {
            return new CreateMinecraftFabricInstanceProvider();
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
        ///    Spawn a new Terraria Server instance creation page.
        /// </summary>
        /// <returns>The user control.</returns>
        public UserControl NewTerrariaServerPage()
        {
            return new CreateTerrariaInstanceProvider();
        }

        /// <summary>
        ///    Spawn a new other executable instance creation page.
        /// </summary>
        /// <returns>The user control.</returns>
        public UserControl NewOtherExecutablePage()
        {
            return new CreateOtherExecutableInstanceProvider();
        }
        #endregion

        public async Task<(ContentDialogResult, ListView)> SelectDaemon()
        {
            var daemonDisplayNames = DaemonsListManager.Get
                .Select(daemon => $"{daemon.FriendlyName} [{(daemon.IsSecure ? "wss" : "ws")}://{daemon.EndPoint}:{daemon.Port}]");
            SimpleStackPanel panel = new();
            ListView listView = new()
            {
                ItemsSource = daemonDisplayNames,
                SelectedIndex = 0,
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(listView);
            ContentDialog dialog = new()
            {
                Title = Lang.Tr["PleaseSelectDaemon"],
                PrimaryButtonText = Lang.Tr["Continue"],
                SecondaryButtonText = Lang.Tr["Cancel"],
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = panel
            };
            var result = await dialog.ShowAsync();
            return (result, listView);
        }
    }
}
