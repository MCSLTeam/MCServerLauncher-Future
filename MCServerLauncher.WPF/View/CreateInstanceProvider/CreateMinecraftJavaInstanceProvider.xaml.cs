using MCServerLauncher.WPF.View.Components;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Helpers.VisualTreeExtensions;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
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
        private void AddJVMArgument(object sender, RoutedEventArgs e)
        {
            JVMArgumentListView.Items.Add(new JVMArgumentItem());
        }
    }
}