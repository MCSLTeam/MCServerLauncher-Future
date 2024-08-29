using iNKORE.UI.WPF.Modern;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Components.SettingCard;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.View.Pages
{
    /// <summary>
    ///    SettingsPage.xaml 的交互逻辑
    /// </summary>
    public partial class SettingsPage
    {
        public SettingsPage()
        {
            InitializeComponent();
            DataContext = this;

            # region Initialize nums

            InitDownloadSourceSelection();
            ResDownload_DownloadThreadCnt.SettingSlider.Value = SettingsManager.AppSettings.Download.ThreadCnt;
            ResDownload_ActionWhenDownloadError.SettingComboBox.SelectedIndex =
                _actionWhenDownloadErrorList.IndexOf(SettingsManager.AppSettings.Download.ActionWhenDownloadError);
            Instance_ActionWhenDeleteConfirm.SettingComboBox.SelectedIndex =
                _actionWhenDeleteConfirmList.IndexOf(SettingsManager.AppSettings.Instance.ActionWhenDeleteConfirm);
            More_LauncherTheme.SettingComboBox.SelectedIndex = _themeList.IndexOf(SettingsManager.AppSettings.App.Theme);
            More_LauncherLanguage.SettingComboBox.SelectedIndex = LanguageManager.LanguageList.IndexOf(SettingsManager.AppSettings.App.Language);

            #endregion

            # region Event handler binding

            InstanceCreation_MinecraftJavaAutoAgreeEula.SettingSwitch.Toggled += OnMinecraftJavaAutoAcceptEulaChanged;
            InstanceCreation_MinecraftJavaAutoDisableOnlineMode.SettingSwitch.Toggled +=
                OnMinecraftJavaAutoSwitchOnlineModeChanged;
            InstanceCreation_MinecraftBedrockAutoDisableOnlineMode.SettingSwitch.Toggled +=
                OnMinecraftBedrockAutoSwitchOnlineModeChanged;
            InstanceCreation_UseMirrorForMinecraftForgeInstall.SettingSwitch.Toggled +=
                OnUseMirrorForMinecraftForgeInstallChanged;
            InstanceCreation_UseMirrorForMinecraftNeoForgeInstall.SettingSwitch.Toggled +=
                OnUseMirrorForMinecraftNeoForgeInstallChanged;
            InstanceCreation_UseMirrorForMinecraftFabricInstall.SettingSwitch.Toggled +=
                OnUseMirrorForMinecraftFabricInstallChanged;
            InstanceCreation_UseMirrorForMinecraftQuiltInstall.SettingSwitch.Toggled +=
                OnUseMirrorForMinecraftQuiltInstallChanged;

            ResDownload_DownloadThreadCnt.SettingSlider.ValueChanged += OnDownloadThreadValueChanged;
            ResDownload_ActionWhenDownloadError.SettingComboBox.SelectionChanged +=
                OnActionWhenDownloadErrorSelectionChanged;

            Instance_ActionWhenDeleteConfirm.SettingComboBox.SelectionChanged +=
                OnActionWhenDeleteConfirmIndexSelectionChanged;

            More_LauncherTheme.SettingComboBox.SelectionChanged += OnLauncherThemeIndexSelectionChanged;
            More_LauncherLanguage.SettingComboBox.SelectionChanged += OnLauncherLanguageIndexSelectionChanged;
            More_FollowStartupForLauncher.SettingSwitch.Toggled += OnFollowStartupForLauncherChanged;
            More_AutoCheckUpdateForLauncher.SettingSwitch.Toggled += OnAutoCheckUpdateForLauncherChanged;

            #endregion

            AboutVersionReplacer.Text = $"Release Version {Assembly.GetExecutingAssembly().GetName().Version}";
#if DEBUG
            AboutVersionReplacer.Text = $"Developer Version {Assembly.GetExecutingAssembly().GetName().Version}";
#endif
        }

        # region MinecraftJavaAutoAcceptEula

        public bool MinecraftJavaAutoAcceptEula
        {
            get => (bool)GetValue(MinecraftJavaAutoAcceptEulaProperty);
            set => SetValue(MinecraftJavaAutoAcceptEulaProperty, value);
        }

        public static readonly DependencyProperty MinecraftJavaAutoAcceptEulaProperty =
            DependencyProperty.Register(
                "MinecraftJavaAutoAcceptEula",
                typeof(bool),
                typeof(SwitchSettingCard),
                new PropertyMetadata(SettingsManager.AppSettings.InstanceCreation.MinecraftJavaAutoAcceptEula)
            );

        private void OnMinecraftJavaAutoAcceptEulaChanged(object sender, RoutedEventArgs e)
        {
            SettingsManager.SaveSetting("InstanceCreation.MinecraftJavaAutoAcceptEula",
                InstanceCreation_MinecraftJavaAutoAgreeEula.SettingSwitch.IsOn);
        }

        # endregion

        # region MinecraftJavaAutoSwitchOnlineMode

        public bool MinecraftJavaAutoSwitchOnlineMode
        {
            get => (bool)GetValue(MinecraftJavaAutoSwitchOnlineModeProperty);
            set => SetValue(MinecraftJavaAutoSwitchOnlineModeProperty, value);
        }

        public static readonly DependencyProperty MinecraftJavaAutoSwitchOnlineModeProperty =
            DependencyProperty.Register(
                "MinecraftJavaAutoSwitchOnlineMode",
                typeof(bool),
                typeof(SwitchSettingCard),
                new PropertyMetadata(SettingsManager.AppSettings.InstanceCreation.MinecraftJavaAutoSwitchOnlineMode)
            );

        private void OnMinecraftJavaAutoSwitchOnlineModeChanged(object sender, RoutedEventArgs e)
        {
            SettingsManager.SaveSetting("InstanceCreation.MinecraftJavaAutoSwitchOnlineMode",
                InstanceCreation_MinecraftJavaAutoDisableOnlineMode.SettingSwitch.IsOn);
        }

        # endregion

        # region MinecraftBedrockAutoSwitchOnlineMode

        public bool MinecraftBedrockAutoSwitchOnlineMode
        {
            get => (bool)GetValue(MinecraftBedrockAutoSwitchOnlineModeProperty);
            set => SetValue(MinecraftBedrockAutoSwitchOnlineModeProperty, value);
        }

        public static readonly DependencyProperty MinecraftBedrockAutoSwitchOnlineModeProperty =
            DependencyProperty.Register(
                "MinecraftBedrockAutoSwitchOnlineMode",
                typeof(bool),
                typeof(SwitchSettingCard),
                new PropertyMetadata(SettingsManager.AppSettings.InstanceCreation.MinecraftBedrockAutoSwitchOnlineMode)
            );

        private void OnMinecraftBedrockAutoSwitchOnlineModeChanged(object sender, RoutedEventArgs e)
        {
            SettingsManager.SaveSetting("InstanceCreation.MinecraftBedrockAutoSwitchOnlineMode",
                InstanceCreation_MinecraftBedrockAutoDisableOnlineMode.SettingSwitch.IsOn);
        }

        #endregion

        #region UseMirrorForMinecraftForgeInstall

        public bool UseMirrorForMinecraftForgeInstall
        {
            get => (bool)GetValue(UseMirrorForMinecraftForgeInstallProperty);
            set => SetValue(UseMirrorForMinecraftForgeInstallProperty, value);
        }

        public static readonly DependencyProperty UseMirrorForMinecraftForgeInstallProperty =
            DependencyProperty.Register(
                "UseMirrorForMinecraftForgeInstall",
                typeof(bool),
                typeof(SwitchSettingCard),
                new PropertyMetadata(SettingsManager.AppSettings.InstanceCreation.UseMirrorForMinecraftForgeInstall)
            );

        private void OnUseMirrorForMinecraftForgeInstallChanged(object sender, RoutedEventArgs e)
        {
            SettingsManager.SaveSetting("InstanceCreation.UseMirrorForMinecraftForgeInstall",
                InstanceCreation_UseMirrorForMinecraftForgeInstall.SettingSwitch.IsOn);
        }

        # endregion

        #region UseMirrorForMinecraftNeoForgeInstall

        public bool UseMirrorForMinecraftNeoForgeInstall
        {
            get => (bool)GetValue(UseMirrorForMinecraftNeoForgeInstallProperty);
            set => SetValue(UseMirrorForMinecraftNeoForgeInstallProperty, value);
        }

        public static readonly DependencyProperty UseMirrorForMinecraftNeoForgeInstallProperty =
            DependencyProperty.Register(
                "UseMirrorForMinecraftNeoForgeInstall",
                typeof(bool),
                typeof(SwitchSettingCard),
                new PropertyMetadata(SettingsManager.AppSettings.InstanceCreation.UseMirrorForMinecraftNeoForgeInstall)
            );

        private void OnUseMirrorForMinecraftNeoForgeInstallChanged(object sender, RoutedEventArgs e)
        {
            SettingsManager.SaveSetting("InstanceCreation.UseMirrorForMinecraftNeoForgeInstall",
                InstanceCreation_UseMirrorForMinecraftNeoForgeInstall.SettingSwitch.IsOn);
        }

        # endregion

        #region UseMirrorForMinecraftFabricInstall

        public bool UseMirrorForMinecraftFabricInstall
        {
            get => (bool)GetValue(UseMirrorForMinecraftFabricInstallProperty);
            set => SetValue(UseMirrorForMinecraftFabricInstallProperty, value);
        }

        public static readonly DependencyProperty UseMirrorForMinecraftFabricInstallProperty =
            DependencyProperty.Register(
                "UseMirrorForMinecraftFabricInstall",
                typeof(bool),
                typeof(SwitchSettingCard),
                new PropertyMetadata(SettingsManager.AppSettings.InstanceCreation.UseMirrorForMinecraftFabricInstall)
            );

        private void OnUseMirrorForMinecraftFabricInstallChanged(object sender, RoutedEventArgs e)
        {
            SettingsManager.SaveSetting("InstanceCreation.UseMirrorForMinecraftFabricInstall",
                InstanceCreation_UseMirrorForMinecraftFabricInstall.SettingSwitch.IsOn);
        }

        # endregion

        #region UseMirrorForMinecraftQuiltInstall

        public bool UseMirrorForMinecraftQuiltInstall
        {
            get => (bool)GetValue(UseMirrorForMinecraftQuiltInstallProperty);
            set => SetValue(UseMirrorForMinecraftQuiltInstallProperty, value);
        }

        public static readonly DependencyProperty UseMirrorForMinecraftQuiltInstallProperty =
            DependencyProperty.Register(
                "UseMirrorForMinecraftQuiltInstall",
                typeof(bool),
                typeof(SwitchSettingCard),
                new PropertyMetadata(SettingsManager.AppSettings.InstanceCreation.UseMirrorForMinecraftQuiltInstall)
            );

        private void OnUseMirrorForMinecraftQuiltInstallChanged(object sender, RoutedEventArgs e)
        {
            SettingsManager.SaveSetting("InstanceCreation.UseMirrorForMinecraftQuiltInstall",
                InstanceCreation_UseMirrorForMinecraftQuiltInstall.SettingSwitch.IsOn);
        }

        # endregion

        # region ResDownloadSource

        private void InitDownloadSourceSelection()
        {
            FastMirrorSrc.IsChecked = SettingsManager.AppSettings.Download.DownloadSource == "FastMirror";
            PolarsMirrorSrc.IsChecked = SettingsManager.AppSettings.Download.DownloadSource == "PolarsMirror";
            ZCloudFileSrc.IsChecked = SettingsManager.AppSettings.Download.DownloadSource == "ZCloudFile";
            MSLAPISrc.IsChecked = SettingsManager.AppSettings.Download.DownloadSource == "MSLAPI";
            MCSLSyncSrc.IsChecked = SettingsManager.AppSettings.Download.DownloadSource == "MCSLSync";
        }

        private void OnResDownloadSourceSelectionChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                SettingsManager.SaveSetting("ResDownload.DownloadSource",
                    ((RadioButton)sender).GetType().GetProperty("Name")?.GetValue(sender).ToString()
                    .Replace("Src", ""));
            }
            catch (ArgumentOutOfRangeException)
            {
                // ignored due to mtfk error
            }
        }

        # endregion

        #region DownloadThreadValue

        public int DownloadThreadValue
        {
            get => (int)GetValue(DownloadThreadValueProperty);
            set => SetValue(DownloadThreadValueProperty, value);
        }

        public static readonly DependencyProperty DownloadThreadValueProperty =
            DependencyProperty.Register(
                "DownloadThreadValue",
                typeof(int),
                typeof(RangeSettingCard),
                new PropertyMetadata(SettingsManager.AppSettings.Download.ThreadCnt)
            );

        private void OnDownloadThreadValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            SettingsManager.SaveSetting("ResDownload.ThreadCnt", (int)ResDownload_DownloadThreadCnt.SettingSlider.Value);
        }

        #endregion

        #region ActionWhenDownloadErrorIndex

        private static readonly List<string> _actionWhenDownloadErrorList = new() { "stop", "retry1", "retry3" };

        public static IEnumerable<string> ActionWhenDownloadError { get; set; } = new List<string>
        {
            LanguageManager.Localize["Settings_ActionWhenDownloadError_Stop"],
            LanguageManager.Localize["Settings_ActionWhenDownloadError_Retry1"],
            LanguageManager.Localize["Settings_ActionWhenDownloadError_Retry3"]
        };

        public int ActionWhenDownloadErrorIndex
        {
            get => (int)GetValue(ActionWhenDownloadErrorIndexProperty);
            set => SetValue(ActionWhenDownloadErrorIndexProperty, value);
        }

        public static readonly DependencyProperty ActionWhenDownloadErrorIndexProperty =
            DependencyProperty.Register(
                "ActionWhenDownloadErrorIndex",
                typeof(int),
                typeof(ComboSettingCard),
                new PropertyMetadata(
                    _actionWhenDownloadErrorList.IndexOf(SettingsManager.AppSettings.Download.ActionWhenDownloadError))
            );

        private void OnActionWhenDownloadErrorSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                SettingsManager.SaveSetting("ResDownload.ActionWhenDownloadError",
                    _actionWhenDownloadErrorList[ResDownload_ActionWhenDownloadError.SettingComboBox.SelectedIndex]);
            }
            catch (ArgumentOutOfRangeException)
            {
                // ignored due to mtfk error
            }
        }

        #endregion

        #region ActionWhenDeleteConfirm

        public static IEnumerable<string> ActionWhenDeleteConfirm { get; set; } = new List<string>
        {
            LanguageManager.Localize["Settings_ActionWhenDeleteConfirm_TypeName"],
            LanguageManager.Localize["Settings_ActionWhenDeleteConfirm_TypeKey"]
        };

        private static readonly List<string> _actionWhenDeleteConfirmList = new() { "name", "key" };

        public int ActionWhenDeleteConfirmIndex
        {
            get => (int)GetValue(ActionWhenDeleteConfirmIndexProperty);
            set => SetValue(ActionWhenDeleteConfirmIndexProperty, value);
        }

        public static readonly DependencyProperty ActionWhenDeleteConfirmIndexProperty =
            DependencyProperty.Register(
                "ActionWhenDeleteConfirmIndex",
                typeof(int),
                typeof(ComboSettingCard),
                new PropertyMetadata(
                    _actionWhenDeleteConfirmList.IndexOf(SettingsManager.AppSettings.Instance.ActionWhenDeleteConfirm))
            );

        private void OnActionWhenDeleteConfirmIndexSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                SettingsManager.SaveSetting("Instance.ActionWhenDeleteConfirm",
                    _actionWhenDeleteConfirmList[Instance_ActionWhenDeleteConfirm.SettingComboBox.SelectedIndex]);
            }
            catch (ArgumentOutOfRangeException)
            {
                // ignored due to mtfk error
            }
        }

        #endregion

        #region LauncherTheme

        public static IEnumerable<string> ThemeForApp { get; set; } = new List<string>
        {
            LanguageManager.Localize["Settings_AppTheme_Auto"],
            LanguageManager.Localize["Settings_AppTheme_Light"],
            LanguageManager.Localize["Settings_AppTheme_Dark"]
        };

        private static readonly List<string> _themeList = new() { "auto", "light", "dark" };

        public int LauncherThemeIndex
        {
            get => (int)GetValue(LauncherThemeIndexProperty);
            set => SetValue(LauncherThemeIndexProperty, value);
        }

        public static readonly DependencyProperty LauncherThemeIndexProperty =
            DependencyProperty.Register(
                "LauncherThemeIndex",
                typeof(int),
                typeof(ComboSettingCard),
                new PropertyMetadata(_themeList.IndexOf(SettingsManager.AppSettings.App.Theme))
            );

        private void OnLauncherThemeIndexSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                SettingsManager.SaveSetting("App.Theme", _themeList[More_LauncherTheme.SettingComboBox.SelectedIndex]);
                ThemeManager.Current.ApplicationTheme = More_LauncherTheme.SettingComboBox.SelectedIndex switch
                {
                    0 => null,
                    1 => ApplicationTheme.Light,
                    2 => ApplicationTheme.Dark,
                    _ => ThemeManager.Current.ApplicationTheme
                };
            }
            catch (ArgumentOutOfRangeException)
            {
                // ignored due to mtfk error
            }
        }

        #endregion

        #region LauncherLanguage

        /// <summary>
        ///     Handle language combo box changes.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnLauncherLanguageIndexSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!SettingsManager.AppSettings.App.IsFirstSetupFinished) return;
            LanguageManager.Localize.ChangeLanguage(new CultureInfo(LanguageManager.LanguageList.ElementAt(More_LauncherLanguage.SettingComboBox.SelectedIndex)));
            OnLanguageChanged();
            SettingsManager.SaveSetting("App.Language", LanguageManager.LanguageList.ElementAt(More_LauncherLanguage.SettingComboBox.SelectedIndex));
        }
        /// <summary>
        /// Update language for ComboBox due to failure of TwoWay binding mode.
        /// </summary>
        public void OnLanguageChanged()
        {
            ResDownload_ActionWhenDownloadError.SettingComboBox.SelectionChanged -=
                OnActionWhenDownloadErrorSelectionChanged;
            var actionWhenDownloadErrorIndex = ResDownload_ActionWhenDownloadError.SettingComboBox.SelectedIndex;
            ActionWhenDownloadError = new List<string>
            {
                LanguageManager.Localize["Settings_ActionWhenDownloadError_Stop"],
                LanguageManager.Localize["Settings_ActionWhenDownloadError_Retry1"],
                LanguageManager.Localize["Settings_ActionWhenDownloadError_Retry3"]
            };
            ResDownload_ActionWhenDownloadError.SettingComboBox.ItemsSource = ActionWhenDownloadError;
            ResDownload_ActionWhenDownloadError.SettingComboBox.SelectedIndex = actionWhenDownloadErrorIndex;
            ResDownload_ActionWhenDownloadError.SettingComboBox.SelectionChanged +=
                OnActionWhenDownloadErrorSelectionChanged;

            Instance_ActionWhenDeleteConfirm.SettingComboBox.SelectionChanged -=
                OnActionWhenDeleteConfirmIndexSelectionChanged;
            var actionWhenDeleteConfirmIndex = Instance_ActionWhenDeleteConfirm.SettingComboBox.SelectedIndex;
            ActionWhenDeleteConfirm = new List<string>
            {
                LanguageManager.Localize["Settings_ActionWhenDeleteConfirm_TypeName"],
                LanguageManager.Localize["Settings_ActionWhenDeleteConfirm_TypeKey"]
            };
            Instance_ActionWhenDeleteConfirm.SettingComboBox.ItemsSource = ActionWhenDeleteConfirm;
            Instance_ActionWhenDeleteConfirm.SettingComboBox.SelectedIndex = actionWhenDeleteConfirmIndex;
            Instance_ActionWhenDeleteConfirm.SettingComboBox.SelectionChanged +=
                OnActionWhenDeleteConfirmIndexSelectionChanged;

            More_LauncherTheme.SettingComboBox.SelectionChanged -= OnLauncherThemeIndexSelectionChanged;
            var themeForAppIndex = More_LauncherTheme.SettingComboBox.SelectedIndex;
            ThemeForApp = new List<string>
            {
                LanguageManager.Localize["Settings_AppTheme_Auto"],
                LanguageManager.Localize["Settings_AppTheme_Light"],
                LanguageManager.Localize["Settings_AppTheme_Dark"]
            };
            More_LauncherTheme.SettingComboBox.ItemsSource = ThemeForApp;
            More_LauncherTheme.SettingComboBox.SelectedIndex = themeForAppIndex;
            More_LauncherTheme.SettingComboBox.SelectionChanged += OnLauncherThemeIndexSelectionChanged;
        }

        #endregion

        # region FollowStartupForLauncher

        public bool FollowStartupForLauncher
        {
            get => (bool)GetValue(FollowStartupForLauncherProperty);
            set => SetValue(FollowStartupForLauncherProperty, value);
        }

        public static readonly DependencyProperty FollowStartupForLauncherProperty =
            DependencyProperty.Register(
                "FollowStartupForLauncher",
                typeof(bool),
                typeof(SwitchSettingCard),
                new PropertyMetadata(SettingsManager.AppSettings.App.FollowStartup)
            );

        private void OnFollowStartupForLauncherChanged(object sender, RoutedEventArgs e)
        {
            SettingsManager.SaveSetting("App.FollowStartup", More_FollowStartupForLauncher.SettingSwitch.IsOn);
        }

        #endregion

        #region AutoCheckUpdateForLauncher

        public bool AutoCheckUpdateForLauncher
        {
            get => (bool)GetValue(AutoCheckUpdateForLauncherProperty);
            set => SetValue(AutoCheckUpdateForLauncherProperty, value);
        }

        public static readonly DependencyProperty AutoCheckUpdateForLauncherProperty =
            DependencyProperty.Register(
                "AutoCheckUpdateForLauncher",
                typeof(bool),
                typeof(SwitchSettingCard),
                new PropertyMetadata(SettingsManager.AppSettings.App.AutoCheckUpdate)
            );

        private void OnAutoCheckUpdateForLauncherChanged(object sender, RoutedEventArgs e)
        {
            SettingsManager.SaveSetting("App.AutoCheckUpdate", More_AutoCheckUpdateForLauncher.SettingSwitch.IsOn);
        }

        # endregion
    }
}