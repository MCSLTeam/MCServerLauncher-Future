using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;
using MCServerLauncher.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Clipboard = MCServerLauncher.WPF.Modules.Clipboard;
using ExceptionWindow = MCServerLauncher.WPF.ExceptionDialog.Window;

namespace MCServerLauncher.WPF
{
    /// <summary>
    ///    App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        // Prevent over-opening
        private Mutex? _mutex;

        public static IServiceProvider Services { get; private set; } = null!;
        public static ViewModelLocator ViewModelLocator { get; private set; } = null!;

        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += AppDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            Services = serviceCollection.BuildServiceProvider();
            ViewModelLocator = new ViewModelLocator(Services);
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<INotificationService, NotificationService>();
            services.AddSingleton<IDaemonConnectionService, DaemonConnectionService>();
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<IPageRegistry>(sp =>
            {
                var registry = new PageRegistry();
                registry.Register("home", typeof(View.Pages.HomePage), Common.Extensibility.PageTarget.MainWindow, 0);
                registry.Register("createInstance", typeof(View.Pages.CreateInstancePage), Common.Extensibility.PageTarget.MainWindow, 1);
                registry.Register("daemonManager", typeof(View.Pages.DaemonManagerPage), Common.Extensibility.PageTarget.MainWindow, 2);
                registry.Register("instanceManager", typeof(View.Pages.InstanceManagerPage), Common.Extensibility.PageTarget.MainWindow, 3);
                registry.Register("resDownload", typeof(View.Pages.ResDownloadPage), Common.Extensibility.PageTarget.MainWindow, 4);
                registry.Register("help", typeof(View.Pages.HelpPage), Common.Extensibility.PageTarget.MainWindow, 5);
                registry.Register("settings", typeof(View.Pages.SettingsPage), Common.Extensibility.PageTarget.MainWindow, 6);
                registry.Register("board", typeof(InstanceConsole.View.Pages.BoardPage), Common.Extensibility.PageTarget.InstanceConsole, 0);
                registry.Register("command", typeof(InstanceConsole.View.Pages.CommandPage), Common.Extensibility.PageTarget.InstanceConsole, 1);
                registry.Register("fileManager", typeof(InstanceConsole.View.Pages.FileManagerPage), Common.Extensibility.PageTarget.InstanceConsole, 2);
                registry.Register("eventTrigger", typeof(InstanceConsole.View.Pages.EventTriggerPage), Common.Extensibility.PageTarget.InstanceConsole, 3);
                registry.Register("componentManager", typeof(InstanceConsole.View.Pages.ComponentManagerPage), Common.Extensibility.PageTarget.InstanceConsole, 4);
                return registry;
            });

            services.AddTransient<DaemonManagerViewModel>();
            services.AddTransient<InstanceManagerViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<CreateInstanceViewModel>();
            services.AddTransient<EventTriggerViewModel>();
            services.AddTransient<CommandPageViewModel>();
            services.AddTransient<InstanceConsole.ViewModels.ComponentManagerViewModel>();
            services.AddTransient<InstanceConsole.ViewModels.InstanceSettingsViewModel>();
        }

#pragma warning disable CS8603 // 可能返回 null 引用。
        public static Version AppVersion => Assembly.GetExecutingAssembly().GetName().Version;
#pragma warning restore CS8603 // 可能返回 null 引用。

        protected async override void OnStartup(StartupEventArgs e)
        {
            await Initializer.InitApp();
            _mutex = new Mutex(true, Assembly.GetExecutingAssembly().GetName().Name, out var createNew);
            if (!createNew)
            {
                MessageBox.Show(Lang.Tr["NotAllowedMultipleProcess"], Lang.Tr["Tip"],
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
                    Dispatcher.Invoke((Delegate)(() => 
                    {
                        MessageBox.Show(
                            Lang.Tr["DeadProcessTip"],
                            "!!!",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }));
                }
                catch
                {

                }
            }
        }
        
        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
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
