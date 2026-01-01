using iNKORE.UI.WPF.Modern.Controls;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Frame = iNKORE.UI.WPF.Modern.Controls.Frame;

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

        /// <summary>
        /// navigate to page by tag and field name (only built-in pages need this)
        /// </summary>
        public static void Navigate(string tag, string field, bool navHide = false)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                // Set Navbar
                var targetItem =
                    mainWindow.NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => Equals(i.Tag, tag)) ??
                    mainWindow.NavView.FooterMenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => Equals(i.Tag, tag));
                if (targetItem != null)
                {
                    mainWindow.NavView.SelectedItem = targetItem;
                }

                // Navigate to Page
                if (mainWindow.FindName("CurrentPage") is Frame currentPage)
                {
                    var pageField = mainWindow.GetType().GetField(field,
                        BindingFlags.NonPublic | BindingFlags.Instance);

                    if (pageField != null)
                    {
                        var page = pageField.GetValue(mainWindow);
                        if (page != null)
                        {
                            currentPage.Navigate(page);
                        }
                    }
                }

                // Hide Navbar
                if (navHide)
                {
                    mainWindow.ToggleNavBarVisibility();
                }
            }
        }
        public static void ToggleNavBarVisibility()
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ToggleNavBarVisibility();
            }
        }
    }
}
