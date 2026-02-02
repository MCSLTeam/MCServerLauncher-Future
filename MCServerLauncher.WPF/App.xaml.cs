using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;
using MCServerLauncher.WPF.Services.Interfaces;
using MCServerLauncher.WPF.View.Pages;
using MCServerLauncher.WPF.ViewModels;
using MCServerLauncher.WPF.ViewModels.FirstSetupHelper;
using MCServerLauncher.WPF.ViewModels.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        private static IHost? _host;

        public static IServiceProvider Services => _host!.Services;

        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += AppDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            // Build DI container
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(ConfigureServices)
                .Build();
        }

        private void ConfigureServices(HostBuilderContext context, IServiceCollection services)
        {
            // Services (Singleton)
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<INotificationService, NotificationService>();
            services.AddSingleton<INavigationService, NavigationService>();

            // Page ViewModels (Transient)
            services.AddTransient<MainWindowViewModel>();
            services.AddTransient<HomePageViewModel>();
            services.AddTransient<CreateInstancePageViewModel>();
            services.AddTransient<DaemonManagerPageViewModel>();
            services.AddTransient<InstanceManagerPageViewModel>();
            services.AddTransient<ResDownloadPageViewModel>();
            services.AddTransient<HelpPageViewModel>();
            services.AddTransient<SettingsPageViewModel>();

            // FirstSetupHelper ViewModels (Transient)
            services.AddTransient<FirstSetupViewModel>();
            services.AddTransient<WelcomeSetupPageViewModel>();
            services.AddTransient<LanguageSetupPageViewModel>();
            services.AddTransient<EulaSetupPageViewModel>();
            services.AddTransient<DaemonSetupPageViewModel>();

            // Provider ViewModels (Transient)
            services.AddTransient<PreCreateInstanceViewModel>();
            services.AddTransient<PreCreateMinecraftInstanceViewModel>();
            services.AddTransient<FastMirrorProviderViewModel>();
            services.AddTransient<PolarsMirrorProviderViewModel>();
            services.AddTransient<MCSLSyncProviderViewModel>();
            services.AddTransient<MSLAPIProviderViewModel>();
            services.AddTransient<RainYunProviderViewModel>();
            services.AddTransient<CreateMinecraftJavaInstanceProviderViewModel>();
            services.AddTransient<CreateMinecraftForgeInstanceProviderViewModel>();
            services.AddTransient<CreateMinecraftNeoForgeInstanceProviderViewModel>();
            services.AddTransient<CreateMinecraftFabricInstanceProviderViewModel>();
            services.AddTransient<CreateMinecraftQuiltInstanceProviderViewModel>();
            services.AddTransient<CreateMinecraftBedrockInstanceProviderViewModel>();
            services.AddTransient<CreateTerrariaInstanceProviderViewModel>();
            services.AddTransient<CreateOtherExecutableInstanceProviderViewModel>();

            // Main Views (Transient)
            services.AddTransient<MainWindow>();
            services.AddTransient<HomePage>();
            services.AddTransient<CreateInstancePage>();
            services.AddTransient<DaemonManagerPage>();
            services.AddTransient<InstanceManagerPage>();
            services.AddTransient<ResDownloadPage>();
            services.AddTransient<HelpPage>();
            services.AddTransient<SettingsPage>();

            // Note: Component views are typically created directly with 'new' as needed
            // They can be registered here if they need dependency injection
        }

#pragma warning disable CS8603 // 可能返回 null 引用。
        public static Version AppVersion => Assembly.GetExecutingAssembly().GetName().Version;
#pragma warning restore CS8603 // 可能返回 null 引用。

        protected async override void OnStartup(StartupEventArgs e)
        {
            // Initialize settings service
            var settingsService = Services.GetRequiredService<ISettingsService>();
            settingsService.Initialize();

            await Initializer.InitApp();
            _mutex = new Mutex(true, Assembly.GetExecutingAssembly().GetName().Name, out var createNew);
            if (!createNew)
            {
                MessageBox.Show(Lang.Tr["NotAllowedMultipleProcess"], Lang.Tr["Tip"],
                    MessageBoxButton.OK, MessageBoxImage.Asterisk);

                Environment.Exit(0);
            }

            // Show main window via DI
            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            _host?.Dispose();
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