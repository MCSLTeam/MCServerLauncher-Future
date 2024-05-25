using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using MCServerLauncher.UI.Helpers;

namespace MCServerLauncher.UI
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
            new Utils().InitApp();
            _mutex = new Mutex(true, Assembly.GetExecutingAssembly().GetName().Name, out bool createNew);
            if (!createNew)
            {
                MessageBox.Show("MCServerLauncher 不支持重复运行。", "提示", MessageBoxButton.OK, MessageBoxImage.Asterisk);

                Environment.Exit(0);
            }
            base.OnStartup(e);
        }
        protected override void OnExit(ExitEventArgs e)
        {
            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
            }
            base.OnExit(e);
        }
    }
}

