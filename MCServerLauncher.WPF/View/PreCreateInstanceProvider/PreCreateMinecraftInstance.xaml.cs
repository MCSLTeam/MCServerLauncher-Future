using iNKORE.UI.WPF.Modern.Media.Animation;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Pages;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.VisualTreeHelper;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
{
    /// <summary>
    ///    PreCreateMinecraftInstance.xaml µÄ½»»¥Âß¼­
    /// </summary>
    public partial class PreCreateMinecraftInstance
    {
        private string _creatingInstanceType = "PreCreating";

        public PreCreateMinecraftInstance()
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
                _ => null
            }, new DrillInNavigationTransitionInfo());
        }

        /// <summary>
        ///    Go back.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void GoPreCreateInstance(object sender, RoutedEventArgs e)
        {                    
            var parent = this.TryFindParent<CreateInstancePage>();
            parent?.CurrentCreateInstance.GoBack();
        }
    }
}