using MCServerLauncher.UI.View.Components;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.UI.Helpers.VisualTreeExtensions;
using MCServerLauncher.UI.Helpers;
using Microsoft.Win32;
using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Helpers;
namespace MCServerLauncher.UI.View.CreateInstanceProvider
{
    /// <summary>
    /// CreateMinecraftJavaInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateMinecraftJavaInstanceProvider : UserControl
    {
        public CreateMinecraftJavaInstanceProvider()
        {
            InitializeComponent();
        }
        
        private void GoPreCreateInstance(object sender, RoutedEventArgs e)
        {
            var parent = this.TryFindParent<CreateInstancePage>();
            parent.CurrentCreateInstance.GoBack();
        }
        private void FinishSetup(object sender, RoutedEventArgs e)
        {
            return;
        }
        private void SelectCore(object sender, RoutedEventArgs e) {
            BasicUtils basicUtils = new BasicUtils();
            string FileName = basicUtils.SelectFile("Jar files (*.jar)|*.jar");
            this.CoreFileSetting.Text = FileName;
        }
        private void SelectJavaFromFile(object sender, RoutedEventArgs e)
        {
            BasicUtils basicUtils = new BasicUtils();
            string FileName = basicUtils.SelectFile("Executeble Java (java.exe)|java.exe");
            this.JavaSetting.Text = FileName;
        }
        private void GoResDownloadPage(object sender, RoutedEventArgs e)
        {
            var mainWindow = this.TryFindParent<MainWindow>();
            mainWindow.CurrentPage.Navigate(new ResDownloadPage());
        }
        private void AddJVMArgument(object sender, RoutedEventArgs e)
        {
            JVMArgumentListView.Items.Add(new JVMArgumentItem());
        }
    }
}
