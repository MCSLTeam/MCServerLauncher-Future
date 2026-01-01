using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Modules.CreateInstance;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.Constants;
using Frame = iNKORE.UI.WPF.Modern.Controls.Frame;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    SelectMinecraftJavaCore.xaml 的交互逻辑
    /// </summary>
    public partial class SelectMinecraftJavaCore : ICreateInstanceStep
    {
        public SelectMinecraftJavaCore()
        {
            InitializeComponent();

            void initialHandler(object sender, TextChangedEventArgs args)
            {
                if (!IsDisposed)
                {
                    SetValue(IsFinishedProperty, !string.IsNullOrWhiteSpace(CoreTextBox.Text));
                }
            }

            CoreTextBox.TextChanged += initialHandler;

            // As you can see, we have to trigger it manually
            VisualTreeHelper.InitStepState(CoreTextBox);
        }

        private bool IsDisposed { get; set; } = false;

        ~SelectMinecraftJavaCore()
        {
            IsDisposed = true;
        }

        public static readonly DependencyProperty IsFinishedProperty = DependencyProperty.Register(
            nameof(IsFinished),
            typeof(bool),
            typeof(SelectMinecraftJavaCore),
            new PropertyMetadata(false, OnStatusChanged));

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SelectMinecraftJavaCore control) return;
            if (e.NewValue is not bool status) return;
            control.StatusShow.Visibility = status switch
            {
                true => Visibility.Visible,
                false => Visibility.Hidden,
            };
        }

        public bool IsFinished
        {
            get => (bool)GetValue(IsFinishedProperty);
            private set => SetValue(IsFinishedProperty, value);
        }

        public CreateInstanceData ActualData
        {
            get => new()
            {
                Type = CreateInstanceDataType.Path,
                Data = CoreTextBox.Text,
            };
        }

        private void SelectCore(object sender, RoutedEventArgs e)
        {
            string? file = Files.SelectFile(
                title: Lang.Tr["SelectMinecraftJavaCore"],
                filter: "Jar Files (*.jar)|*.jar"
            );
            if (file != null)
            {
                CoreTextBox.Text = file;
                SetValue(IsFinishedProperty, true);
            }
            else
            {
                if (!string.IsNullOrEmpty(CoreTextBox.Text))
                {
                    CoreTextBox.Text = string.Empty;
                    SetValue(IsFinishedProperty, false);
                }
            }
        }

        private void GoResDownload(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                if (mainWindow.FindName("CurrentPage") is Frame currentPage)
                {
                    var resDownloadField = mainWindow.GetType().GetField("_resDownload",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                    if (resDownloadField != null)
                    {
                        var resDownloadPage = resDownloadField.GetValue(mainWindow);
                        if (resDownloadPage != null)
                        {
                            mainWindow.ToggleNavBarVisibility();
                            currentPage.Navigate(resDownloadPage);
                            Button backButton = new()
                            {
                                Content = Lang.Tr["Back"],
                                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                            };
                            backButton.Click += GoResDownloadBack;

                            Notification.Push(
                                title: Lang.Tr["Tip"],
                                message: Lang.Tr["SelectMinecraftJavaCoreGoResDownloadMessage"],
                                isClosable: false,
                                severity: InfoBarSeverity.Informational,
                                position: InfoBarPosition.Top,
                                durationMs: -1,
                                systemNotify: false,
                                button: backButton,
                                isButtonRegisterClose: true
                            );
                        }
                    }
                }
            }
        }

        private void GoResDownloadBack(object sender, RoutedEventArgs e)
        {
            VisualTreeHelper.Navigate("MCServerLauncher.WPF.View.Pages.ResDownloadPage", "_resDownload", true);
        }
    }
}