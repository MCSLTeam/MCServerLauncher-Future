using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MCServerLauncher.WPF.View.Pages
{
    /// <summary>
    ///    SettingsPage.xaml 的交互逻辑
    /// </summary>
    public partial class SettingsPage
    {
        private int _debugClickCount = 0;

        public SettingsPage(SettingsPageViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Note: Most logic has been moved to SettingsPageViewModel.
            // The ViewModel handles all settings through data binding and property change handlers.
            // Remaining code here is only for XAML compatibility with legacy RadioButton event handlers.
        }

        // Keep debug mode toggle for development purposes (UI-only concern)
        private void CheckDebugMode(object sender, MouseButtonEventArgs e)
        {
            _debugClickCount++;
            if (_debugClickCount >= 7)
            {
                _debugClickCount = 0;
                MessageBox.Show("Debug Mode Activated", "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // This event handler is kept for XAML compatibility with RadioButton Checked events
        // The actual state is managed by ViewModel through IsFastMirrorChecked, IsPolarsMirrorChecked, etc.
        private void OnResDownloadSourceSelectionChanged(object sender, RoutedEventArgs e)
        {
            // The ViewModel automatically handles the download source change through two-way binding
            // This handler is only here for XAML compatibility
        }
    }
}
