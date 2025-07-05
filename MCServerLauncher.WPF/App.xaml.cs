using MCServerLauncher.WPF.Modules;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Clipboard = MCServerLauncher.WPF.Modules.Clipboard;
using ExceptionWindow = MCServerLauncher.WPF.ExceptionDialog.Window;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;

namespace MCServerLauncher.WPF
{
    /// <summary>
    ///    App.xaml 的交互逻辑
    ///    (不要删掉冗余的 : Application 继承，否则无法正确使用 docfx 生成文档)
    /// </summary>
    public partial class App : Application
    {
        // Prevent over-opening
        private Mutex? _mutex;

        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += AppDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
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

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Clipboard.SetText(e.Exception.ToString());
            new ExceptionWindow(e.Exception.ToString()).ShowDialog();
            e.Handled = true;
        }

        private void AppDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            string exceptionString = exception?.ToString() ?? "Unknown Err";
            
            try
            {
                Clipboard.SetText(exceptionString);
                Dispatcher.Invoke(() => 
                {
                    new ExceptionWindow(exceptionString).ShowDialog();
                });
            }
            catch
            {
                try
                {
                    Dispatcher.Invoke(() => 
                    {
                        MessageBox.Show(
                            LanguageManager.Localize["DeadProcessTip"],
                            "!!!", 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Error);
                    });
                }
                catch
                {

                }
            }
        }
        
        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            
            var exception = e.Exception;
            string exceptionString = exception?.ToString() ?? "Unknown Task Err";
            
            try
            {
                Clipboard.SetText(exceptionString);
                
                Dispatcher.Invoke(() => 
                {
                    new ExceptionWindow(exceptionString).ShowDialog();
                });
            }
            catch
            {

            }
        }
    }
}