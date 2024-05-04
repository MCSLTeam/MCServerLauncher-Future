using System.Windows;
using System.Windows.Controls;

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
            var parent = (CreateInstancePage)Parent;
            parent.CurrentCreateInstance.GoBack();
        }
        private void FinishSetup(object sender, RoutedEventArgs e)
        {
            return;
        }
    }
}
