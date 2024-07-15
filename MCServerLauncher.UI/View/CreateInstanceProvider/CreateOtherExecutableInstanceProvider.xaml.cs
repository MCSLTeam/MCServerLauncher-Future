using System.Windows;
using System.Windows.Controls;
using MCServerLauncher.UI.Helpers;

namespace MCServerLauncher.UI.View.CreateInstanceProvider
{
    /// <summary>
    /// CreateOtherExecutableInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateOtherExecutableInstanceProvider : UserControl
    {
        public CreateOtherExecutableInstanceProvider()
        {
            InitializeComponent();
        }
        private void GoPreCreateInstance(object sender, RoutedEventArgs e)
        {
            var parent = this.TryFindParent<CreateInstancePage>();
            parent.CurrentCreateInstance.GoBack();
        }
        private void SelectArchive(object sender, RoutedEventArgs e)
        {
            BasicUtils basicUtils = new BasicUtils();
            string FileName = basicUtils.SelectFile("Archive files (*.zip)|*.zip"); 
            this.DepensSetting.Text = FileName;
        }
        private void FinishSetup(object sender, RoutedEventArgs e)
        {
            return;
        }
    }
}
