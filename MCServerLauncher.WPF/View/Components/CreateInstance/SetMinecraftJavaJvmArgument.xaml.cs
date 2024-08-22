using System.Windows;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    /// SetMinecraftJavaJvmArgument.xaml 的交互逻辑
    /// </summary>
    public partial class SetMinecraftJavaJvmArgument
    {
        public SetMinecraftJavaJvmArgument()
        {
            InitializeComponent();
        }

        private void AddJvmArgument(object sender, RoutedEventArgs e)
        {
            JVMArgumentListView.Items.Add(new JvmArgumentItem());
        }
    }
}
