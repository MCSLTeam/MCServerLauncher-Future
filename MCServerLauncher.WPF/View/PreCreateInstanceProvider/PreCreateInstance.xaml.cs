using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Pages;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.VisualTreeHelper;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
{
    /// <summary>
    ///    PreCreateInstance.xaml µÄ½»»¥Âß¼­
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
        private async void GoCreateNewInstance(object sender, RoutedEventArgs e)
        {
            _creatingInstanceType = ((Button)sender).Name;
            PreCreateInstanceGrid.IsEnabled = false;
            if (_creatingInstanceType == "PreCreating") return;

            var parent = this.TryFindParent<CreateInstancePage>();
            
            while (true)
            {
                (ContentDialogResult result, var listView) = await parent!.SelectDaemon();
                if (result != ContentDialogResult.Primary)
                {
                    PreCreateInstanceGrid.IsEnabled = true;
                    return;
                }

                if (listView != null && listView.SelectedItem != null)
                {
                    Constants.SelectedDaemon = listView.SelectedItem.ToString() ?? string.Empty;
                    var daemon = DaemonsListManager.MatchDaemonBySelection(Constants.SelectedDaemon);
                    var conn = await DaemonsWsManager.Get(daemon);
                    if (conn == null)
                    {
                        var dialog = new ContentDialog()
                        {
                            Title = Lang.Tr["ConnectDaemonFailedTip"],
                            Content = Lang.Tr["ConnectDaemonFailedSubTip"],
                            DefaultButton = ContentDialogButton.Primary,
                            PrimaryButtonText = Lang.Tr["SelectOtherDaemon"],
                            CloseButtonText = Lang.Tr["Cancel"]
                        };
                        var retryResult = await dialog.ShowAsync();
                        if (retryResult != ContentDialogResult.Primary)
                        {
                            PreCreateInstanceGrid.IsEnabled = true;
                            return;
                        }
                        continue;
                    }

                    parent?.CurrentCreateInstance.Navigate(_creatingInstanceType switch
                    {
                        "MinecraftServer" => parent.NewPreMinecraftInstancePage(),
                        "TerrariaGameServer" => parent.NewTerrariaServerPage(),
                        "OtherExecutable" => parent.NewOtherExecutablePage(),
                        _ => null
                    }, new DrillInNavigationTransitionInfo());
                    break;
                }
            }
            PreCreateInstanceGrid.IsEnabled = true;
        }
    }
}