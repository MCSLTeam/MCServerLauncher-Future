using iNKORE.UI.WPF.Modern.Common.IconKeys;
using CommunityToolkit.Mvvm.Input;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.CreateInstanceProvider;
using MCServerLauncher.WPF.ViewModels;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace MCServerLauncher.WPF.View.Pages
{
    public partial class CreateInstancePage
    {
        public readonly PreCreateInstance PreCreateInstance = new();
        private readonly CreateInstanceViewModel _viewModel;

        public CreateInstancePage()
        {
            InitializeComponent();
            _viewModel = App.ViewModelLocator.CreateInstance;
            DataContext = _viewModel;
            StopTipLayer.ButtonCommand = new AsyncRelayCommand(OpenDaemonManagerConnectionAsync);
            CurrentCreateInstance.Content = PreCreateInstance;
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible) ValidateFuncAvailable();
            };
        }

        private void ValidateFuncAvailable()
        {
            _viewModel.CheckDaemonAvailabilityCommand.Execute(null);
            if (!_viewModel.IsDaemonAvailable)
            {
                ShowNoDaemonLayer();
                return;
            }
            StopTipLayer.Visibility = Visibility.Collapsed;
            CurrentCreateInstance.Visibility = Visibility.Visible;
        }

        private void ShowNoDaemonLayer()
        {
            CurrentCreateInstance.Visibility = Visibility.Collapsed;
            StopTipLayer.Visibility = Visibility.Collapsed;
            StopTipLayer.Symbol = "❌";
            StopTipLayer.StopTip = Lang.Tr["FuncDisabled"];
            StopTipLayer.StopDescription = Lang.Tr["FuncDisabledReason_NoDaemon"];
            StopTipLayer.ButtonIcon = SegoeFluentIcons.ConnectApp;
            StopTipLayer.ButtonText = Lang.Tr["ConnectDaemon"];
            StopTipLayer.ButtonCommand = new AsyncRelayCommand(OpenDaemonManagerConnectionAsync);
            StopTipLayer.Visibility = Visibility.Visible;
        }

        #region Create Instance Pages

        public UserControl NewPreMinecraftInstancePage() => new PreCreateMinecraftInstance();
        public UserControl NewMinecraftJavaServerPage() => new CreateMinecraftJavaInstanceProvider();
        public UserControl NewMinecraftForgeServerPage() => new CreateMinecraftForgeInstanceProvider();
        public UserControl NewMinecraftNeoForgeServerPage() => new CreateMinecraftNeoForgeInstanceProvider();
        public UserControl NewMinecraftFabricServerPage() => new CreateMinecraftFabricInstanceProvider();
        public UserControl NewMinecraftQuiltServerPage() => new CreateMinecraftQuiltInstanceProvider();
        public UserControl NewMinecraftBedrockServerPage() => new CreateMinecraftBedrockInstanceProvider();
        public UserControl NewTerrariaServerPage() => new CreateTerrariaInstanceProvider();
        public UserControl NewOtherExecutablePage() => new CreateOtherExecutableInstanceProvider();

        #endregion

        public async Task<(ContentDialogResult, System.Windows.Controls.ListView)> SelectDaemon()
        {
            return await _viewModel.SelectDaemonAsync();
        }

        private async Task OpenDaemonManagerConnectionAsync()
        {
            await VisualTreeHelper.NavigateToDaemonManagerAndOpenConnectionAsync();
        }
    }
}
