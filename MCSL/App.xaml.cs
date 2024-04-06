using System;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace MCServerLauncher
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // 添加崩溃处理事件
            DispatcherUnhandledException += (s, e) =>
            {
                MessageBox.Show("MCServerLauncher 发生了未经处理的异常：\n\n" + e.Exception.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true; // 设置为已处理，阻止应用程序崩溃
            };
        }

        //以创建Mutex的方式防止同目录多开，避免奇奇怪怪的文件占用错误
        private Mutex _mutex;
        protected override void OnStartup(StartupEventArgs e)
        {
            bool createNew;
            _mutex = new Mutex(true, Assembly.GetExecutingAssembly().GetName().Name, out createNew);
            if (!createNew)
            {
                MessageBox.Show("MCServerLauncher 已在运行，若与实际不符请检查后台程序。", "提示");
                Environment.Exit(0);
            }
            base.OnStartup(e);
        }
    }
}

