using System;
using System.Windows;
using System.Windows.Controls;
using MCServerLauncher.UI.Helpers;

namespace MCServerLauncher.UI.View.CreateInstanceProvider
{
    /// <summary>
    /// CreateMinecraftBedrockInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateMinecraftBedrockInstanceProvider : UserControl
    {
        public CreateMinecraftBedrockInstanceProvider()
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
        private void SelectArchive(object sender, RoutedEventArgs e)
        {
            BasicUtils basicUtils = new BasicUtils();
            string FileName = basicUtils.SelectFile("Archive files (*.*)|*.*"); //idk what the archive format is
            this.ArchiveSetting.Text = FileName;
        }
        private void GoResDownloadPage(object sender, RoutedEventArgs e)
        {
            var mainWindow = this.TryFindParent<MainWindow>();
            mainWindow.CurrentPage.Navigate(new ResDownloadPage());
        }
    }
}
