using ConsoleWindow = MCServerLauncher.WPF.InstanceConsole.Window;
namespace MCServerLauncher.WPF.View.Pages
{
    /// <summary>
    ///    HomePage.xaml 的交互逻辑
    /// </summary>
    public partial class HomePage
    {
        public HomePage()
        {
            InitializeComponent();
        }

        private void ShowConsoleWindow(object sender, System.Windows.RoutedEventArgs e)
        {
            new ConsoleWindow().Show();
        }
    }
}