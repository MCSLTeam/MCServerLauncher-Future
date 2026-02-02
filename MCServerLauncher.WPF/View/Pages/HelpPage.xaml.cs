using MCServerLauncher.WPF.ViewModels;

namespace MCServerLauncher.WPF.View.Pages

{
    /// <summary>
    ///    HelpPage.xaml 的交互逻辑
    /// </summary>
    public partial class HelpPage
    {
        public HelpPage(HelpPageViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}