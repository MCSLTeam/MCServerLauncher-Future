﻿using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using MCServerLauncher.WPF.Helpers;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;

namespace MCServerLauncher.WPF
{
    /// <summary>
    ///    App.xaml 的交互逻辑
    /// </summary>
    public partial class App
    {
        // Prevent over-opening
        private Mutex _mutex;

        public App()
        {
            // Crash handler
            DispatcherUnhandledException += (s, e) =>
            {
                Clipboard.SetText(e.Exception.ToString());
                MessageBox.Show($"{LanguageManager.Instance["ErrorDialogStackCopiedTip"]}\n\n{e.Exception}", LanguageManager.Instance["ErrorDialogTitle"],
                    MessageBoxButton.OK);
                e.Handled = true; // Set `Handled` to `true` to prevent from exiting
            };
        }

        public static Version AppVersion => Assembly.GetExecutingAssembly().GetName().Version;

        protected override void OnStartup(StartupEventArgs e)
        {
            new BasicUtils().InitApp();
            _mutex = new Mutex(true, Assembly.GetExecutingAssembly().GetName().Name, out var createNew);
            if (!createNew)
            {
                MessageBox.Show(LanguageManager.Instance["NotAllowedOverOpening"], LanguageManager.Instance["Tip"], MessageBoxButton.OK, MessageBoxImage.Asterisk);

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