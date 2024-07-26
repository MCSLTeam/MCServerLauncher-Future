using System.Windows;
using MCServerLauncher.WPF.Main.View.Components;
using static MCServerLauncher.WPF.Main.Helpers.VisualTreeExtensions;

namespace MCServerLauncher.WPF.Main.View.CreateInstanceProvider
{
    /// <summary>
    ///     CreateMinecraftJavaInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateMinecraftJavaInstanceProvider
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
        }

        private void AddJvmArgument(object sender, RoutedEventArgs e)
        {
            JVMArgumentListView.Items.Add(new JVMArgumentItem());
        }
    }
}