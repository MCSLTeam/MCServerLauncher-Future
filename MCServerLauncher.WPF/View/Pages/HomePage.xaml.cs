using MCServerLauncher.WPF.ViewModels;

namespace MCServerLauncher.WPF.View.Pages
{
    /// <summary>
    ///    HomePage.xaml 的交互逻辑
    /// </summary>
    public partial class HomePage
    {
        public HomePage(HomePageViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}