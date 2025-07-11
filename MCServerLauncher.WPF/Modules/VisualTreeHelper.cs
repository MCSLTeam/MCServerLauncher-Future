using System.Windows;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.Modules
{
    internal static class VisualTreeHelper
    {
        public static T? TryFindParent<T>(this DependencyObject child) where T : DependencyObject
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T parentType) return parentType;
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }

            return null;
        }
        public static void InitStepState(ComboBox comboBox)
        {
            comboBox.Items.Add("1test");
            comboBox.SelectedIndex = 0;
            comboBox.Items.Remove("1test");
        }
        public static void InitStepState(TextBox textBox)
        {
            textBox.Text = "1";
            textBox.Text = string.Empty;
        }
    }
}
