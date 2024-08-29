using System.Windows;

namespace MCServerLauncher.WPF.Modules
{
    internal static class VisualTreeHelper
    {
        public static T TryFindParent<T>(this DependencyObject child) where T : DependencyObject
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T parentType) return parentType;
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }

            return null;
        }
    }
}
