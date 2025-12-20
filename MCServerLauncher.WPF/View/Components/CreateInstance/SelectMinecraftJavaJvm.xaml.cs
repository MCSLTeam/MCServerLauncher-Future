using iNKORE.UI.WPF.Controls;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.DaemonClient.Connection;
using MCServerLauncher.WPF.Modules;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.Constants;
using ListView = iNKORE.UI.WPF.Modern.Controls.ListView;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    SelectMinecraftJavaJvm.xaml 的交互逻辑
    /// </summary>
    public partial class SelectMinecraftJavaJvm : ICreateInstanceStep
    {
        public SelectMinecraftJavaJvm()
        {
            InitializeComponent();

            void initialHandler(object sender, TextChangedEventArgs args)
            {
                if (!IsDisposed)
                {
                    SetValue(IsFinishedProperty, !string.IsNullOrWhiteSpace(JavaRuntimeTextBox.Text));
                }
            }

            JavaRuntimeTextBox.TextChanged += initialHandler;

            // As you can see, we have to trigger it manually
            VisualTreeHelper.InitStepState(JavaRuntimeTextBox);
        }

        private bool IsDisposed { get; set; } = false;

        ~SelectMinecraftJavaJvm()
        {
            IsDisposed = true;
        }

        public static readonly DependencyProperty IsFinishedProperty = DependencyProperty.Register(
            nameof(IsFinished),
            typeof(bool),
            typeof(SelectMinecraftJavaJvm),
            new PropertyMetadata(false, OnStatusChanged));

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SelectMinecraftJavaJvm control) return;
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
                Data = JavaRuntimeTextBox.Text,
            };
        }

        private async void GetJvmResult(object sender, RoutedEventArgs e)
        {
            Notification.Push(
                Lang.Tr["PleaseWait"],
                Lang.Tr["SearchingJvmTip"],
                false,
                InfoBarSeverity.Informational,
                InfoBarPosition.Top,
                2000,
                false
            );
            IDaemon? daemon = null;
            DaemonConfigModel daemonConfig = DaemonsListManager.MatchDaemonBySelection(SelectedDaemon);
            daemon = await DaemonsWsManager.Get(daemonConfig);
#pragma warning disable CS8604 // 引用类型参数可能为 null。
            JavaInfo[] jvms = await Task.Run(() => DaemonExtensions.GetJavaListAsync(daemon));
#pragma warning restore CS8604 // 引用类型参数可能为 null。
            if (jvms.Length > 0)
            {
                var jvmDisplayNames = jvms.Select(info => $"({info.Version}, {info.Architecture}) {info.Path}");
                ScrollViewerEx scroll = new()
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 500
                };
                SimpleStackPanel panel = new();
                ListView listView = new()
                {
                    ItemsSource = jvmDisplayNames,
                    SelectedIndex = 0,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                panel.Children.Add(listView);
                scroll.Content = panel;
                ContentDialog dialog = new()
                {
                    Title = Lang.Tr["PleaseSelectJvm"],
                    PrimaryButtonText = Lang.Tr["Continue"],
                    SecondaryButtonText = Lang.Tr["Cancel"],
                    DefaultButton = ContentDialogButton.Primary,
                    FullSizeDesired = false,
                    Content = scroll,
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    if (listView != null && listView.SelectedItem != null)
                    {
                        JavaRuntimeTextBox.Text = jvms[listView.SelectedIndex].Path;
                    }
                }
            }
        }
    }
}