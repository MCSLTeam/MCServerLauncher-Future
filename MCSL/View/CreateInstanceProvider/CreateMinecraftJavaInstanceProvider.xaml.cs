using MCServerLauncher.View.Components;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MCServerLauncher.View.CreateInstanceProvider
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

        private void GoCreateInstance(object sender, RoutedEventArgs e)
        {
            var parent = this.TryFindParent<CreateInstancePage>();
            parent.GoCreateInstance(sender, e);
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
public static class VisualTreeExtensions
{
    public static T TryFindParent<T>(this DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T parentType)
            {
                return parentType;
            }
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}