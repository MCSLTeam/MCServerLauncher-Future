using iNKORE.UI.WPF.Modern.Media.Animation;
using MCServerLauncher.WPF.View.Pages;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.VisualTreeHelper;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
{
    /// <summary>
    ///    PreCreateInstance.xaml �Ľ����߼�
    /// </summary>
    public partial class PreCreateInstance
    {
        private string _creatingInstanceType = "PreCreating";

        public PreCreateInstance()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    Trigger for navigating to the creation page.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void GoCreateNewInstance(object sender, RoutedEventArgs e)
        {
            _creatingInstanceType = ((Button)sender).Name;
            if (_creatingInstanceType == "PreCreating") return;

            var parent = this.TryFindParent<CreateInstancePage>();
            parent?.CurrentCreateInstance.Navigate(_creatingInstanceType switch
            {
                "MinecraftJavaServer" => parent.NewMinecraftJavaServerPage(),
                "MinecraftForgeServer" => parent.NewMinecraftForgeServerPage(),
                "MinecraftNeoForgeServer" => parent.NewMinecraftNeoForgeServerPage(),
                "MinecraftFabricServer" => parent.NewMinecraftFabricServerPage(),
                "MinecraftQuiltServer" => parent.NewMinecraftQuiltServerPage(),
                "MinecraftBedrockServer" => parent.NewMinecraftBedrockServerPage(),
                "TerrariaGameServer" => parent.NewTerrariaServerPage(),
                "OtherExecutable" => parent.NewOtherExecutablePage(),
                _ => null
            }, new DrillInNavigationTransitionInfo());
        }
    }
}