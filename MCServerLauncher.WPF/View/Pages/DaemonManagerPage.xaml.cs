using MCServerLauncher.WPF.ViewModels;

namespace MCServerLauncher.WPF.View.Pages
{
    public partial class DaemonManagerPage
    {
        private readonly DaemonManagerViewModel _viewModel;

        public DaemonManagerPage()
        {
            InitializeComponent();
            _viewModel = App.ViewModelLocator.DaemonManager;
            DataContext = _viewModel;

            IsVisibleChanged += async (s, e) =>
            {
                if (IsVisible)
                {
                    await _viewModel.RefreshCommand.ExecuteAsync(null);
                }
            };
        }
    }
}
