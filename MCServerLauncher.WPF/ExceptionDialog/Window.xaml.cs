using System;
using System.Diagnostics;
using System.Windows;

namespace MCServerLauncher.WPF.ExceptionDialog
{
    /// <summary>
    ///    Window.xaml 的交互逻辑
    /// </summary>
    public partial class Window
    {

        public Window(string stack)
        {
            InitializeComponent();
            StackLogTextBox.Text = stack;
        }

        private void Feedback(object sender, RoutedEventArgs e)
        {
            Process.Start("https://github.com/MCSLTeam/MCServerLauncher-Future/issues/new");
        }

        private void CloseWindow(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RestartApp(object sender, RoutedEventArgs e)
        {
            Process.Start(Application.ResourceAssembly.Location);
            Environment.Exit(0);
        }

        private void ExitApp(object sender, RoutedEventArgs e)
        {
            Environment.Exit(1);
        }
    }
}