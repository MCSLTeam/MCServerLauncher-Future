using System.Windows;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;

namespace MCServerLauncher.WPF.Console
{
    /// <summary>
    ///     App.xaml 的交互逻辑
    /// </summary>
    public partial class App
    {
        public App()
        {
            // 添加崩溃处理事件
            DispatcherUnhandledException += (s, e) =>
            {
                Clipboard.SetText(e.Exception.ToString());
                MessageBox.Show($"堆栈已复制到剪贴板。您可直接进入 GitHub 进行反馈。\n\n{e.Exception}", "MCServerLauncher WPF 遇到错误", MessageBoxButton.OK);
                e.Handled = true; // 设置为已处理，阻止应用程序崩溃
            };
        }
    }
}