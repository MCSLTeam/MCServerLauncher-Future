using MCServerLauncher.WPF.Modules;
using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using Clipboard = MCServerLauncher.WPF.Modules.Clipboard;
using ExceptionWindow = MCServerLauncher.WPF.ExceptionDialog.Window;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;

namespace MCServerLauncher.WPF
{
    /// <summary>
    ///    App.xaml 的交互逻辑
    ///    (不要删掉冗余的 : Application继承，否则无法正确使用docfx生成文档)
    /// </summary>
    public partial class App : Application
    {
        // Prevent over-opening
        private Mutex? _mutex;

        public App()
        {
            // Crash handler
            DispatcherUnhandledException += (s, e) =>
            {
                Clipboard.SetText(e.Exception.ToString());
                new ExceptionWindow(e.Exception.ToString()).ShowDialog();
                e.Handled = true; // Set `Handled` to `true` to prevent from crashes
            };
        }

        public static Version AppVersion => Assembly.GetExecutingAssembly().GetName().Version;

        protected override void OnStartup(StartupEventArgs e)
        {
            new Initializer().InitApp();
            _mutex = new Mutex(true, Assembly.GetExecutingAssembly().GetName().Name, out var createNew);
            if (!createNew)
            {
                MessageBox.Show(LanguageManager.Localize["NotAllowedOverOpening"], LanguageManager.Localize["Tip"],
                    MessageBoxButton.OK, MessageBoxImage.Asterisk);

                Environment.Exit(0);
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            base.OnExit(e);
        }
    }
}