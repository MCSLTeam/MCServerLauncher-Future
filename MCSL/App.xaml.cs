using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace MCServerLauncher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // 添加崩溃处理事件
            DispatcherUnhandledException += (s, e) =>
            {
                MessageBox.Show("程序在运行的时候发生了异常，异常代码：\n" + e.Exception.Message + "\n若软件闪退，请联系作者进行反馈", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show("MCServerLauncher 不支持重复运行", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                Environment.Exit(0);
            }
            base.OnStartup(e);
        }
    }
}

