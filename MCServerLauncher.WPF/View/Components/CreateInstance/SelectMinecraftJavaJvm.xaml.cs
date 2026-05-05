using iNKORE.UI.WPF.Controls;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.DaemonClient.Connection;
using MCServerLauncher.WPF.Modules;
using System;
using System.Collections.Generic;
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
        private JavaInfo[]? _cachedJavaList;
        private bool _isLoadingJavaList;
        private bool _suppressEvents;
        private int _pendingSelectionIndex = -1;

        public SelectMinecraftJavaJvm()
        {
            InitializeComponent();

            JavaRuntimeComboBox.SelectionChanged += OnJavaRuntimeSelectionChanged;
            JavaRuntimeComboBox.DropDownClosed += OnJavaRuntimeDropDownClosed;
            JavaRuntimeComboBox.Loaded += OnJavaRuntimeComboBoxLoaded;

            // Load Java list cache on initialization
            _ = LoadJavaListAsync();
        }

        private void OnJavaRuntimeComboBoxLoaded(object sender, RoutedEventArgs e)
        {
            if (JavaRuntimeComboBox.Template.FindName("PART_EditableTextBox", JavaRuntimeComboBox) is TextBox textBox)
            {
                textBox.TextChanged += OnJavaRuntimeTextChanged;
                // Initialize step state on the TextBox, not the ComboBox
                Modules.VisualTreeHelper.InitStepState(textBox);
            }
        }

        private void OnJavaRuntimeTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_suppressEvents)
            {
                UpdateIsFinishedStatus();
            }
        }

        private void OnJavaRuntimeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsDisposed) return;

            // Store the selected index for processing in DropDownClosed
            if (e.AddedItems.Count > 0)
            {
                _pendingSelectionIndex = JavaRuntimeComboBox.SelectedIndex;
            }
        }

        private void OnJavaRuntimeDropDownClosed(object sender, EventArgs e)
        {
            if (IsDisposed) return;

            // Process the pending selection after dropdown closes
            if (_pendingSelectionIndex >= 0 && _cachedJavaList != null && _pendingSelectionIndex < _cachedJavaList.Length)
            {
                _suppressEvents = true;

                // Extract only the path
                var path = _cachedJavaList[_pendingSelectionIndex].Path;
                JavaRuntimeComboBox.Text = path;

                _suppressEvents = false;
                _pendingSelectionIndex = -1;

                UpdateIsFinishedStatus();
            }
        }

        private void UpdateIsFinishedStatus()
        {
            if (!IsDisposed)
            {
                SetValue(IsFinishedProperty, !string.IsNullOrWhiteSpace(JavaRuntimeComboBox.Text));
            }
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
                Data = JavaRuntimeComboBox.Text,
            };
        }

        private async Task LoadJavaListAsync()
        {
            if (_isLoadingJavaList || _cachedJavaList != null)
                return;

            _isLoadingJavaList = true;

            try
            {
                var daemonConfig = DaemonsListManager.MatchDaemonBySelection(SelectedDaemon);
                var daemon = await DaemonsWsManager.Get(daemonConfig);

                if (daemon != null)
                {
                    _cachedJavaList = await daemon.GetJavaListAsync();
                    PopulateJavaComboBox();
                }
            }
            catch (Exception)
            {
                // Silently fail, user can still manually enter or search
            }
            finally
            {
                _isLoadingJavaList = false;
            }
        }

        private void PopulateJavaComboBox()
        {
            if (_cachedJavaList == null || _cachedJavaList.Length == 0)
                return;

            JavaRuntimeComboBox.Items.Clear();
            foreach (var jvm in _cachedJavaList)
            {
                JavaRuntimeComboBox.Items.Add($"({jvm.Version}, {jvm.Architecture}) {jvm.Path}");
            }
        }

        private async void GetJvmResult(object sender, RoutedEventArgs e)
        {
            var menuItem = (MenuItem)sender;
            menuItem.IsEnabled = false;

            Notification.Push(
                Lang.Tr["PleaseWait"],
                Lang.Tr["SearchingJvmTip"],
                false,
                InfoBarSeverity.Informational,
                InfoBarPosition.Top,
                2000,
                false
            );

            try
            {
                var daemonConfig = DaemonsListManager.MatchDaemonBySelection(SelectedDaemon);
                var daemon = await DaemonsWsManager.Get(daemonConfig);

                if (daemon == null)
                {
                    Notification.Push(
                        Lang.Tr["Error"],
                        Lang.Tr["DaemonConnectionError"],
                        true,
                        InfoBarSeverity.Error,
                        InfoBarPosition.Top,
                        3000,
                        false
                    );
                    return;
                }

                var jvms = await daemon.GetJavaListAsync();
                _cachedJavaList = jvms;

                if (jvms.Length > 0)
                {
                    PopulateJavaComboBox();

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
                            JavaRuntimeComboBox.Text = jvms[listView.SelectedIndex].Path;
                        }
                    }
                }
                else
                {
                    Notification.Push(
                        Lang.Tr["Info"],
                        Lang.Tr["NoJavaFound"],
                        true,
                        InfoBarSeverity.Warning,
                        InfoBarPosition.Top,
                        3000,
                        false
                    );
                }
            }
            catch (Exception ex)
            {
                Notification.Push(
                    Lang.Tr["Error"],
                    $"{Lang.Tr["SearchJavaError"]}: {ex.Message}",
                    true,
                    InfoBarSeverity.Error,
                    InfoBarPosition.Top,
                    5000,
                    false
                );
            }
            finally
            {
                menuItem.IsEnabled = true;
            }
        }
    }
}