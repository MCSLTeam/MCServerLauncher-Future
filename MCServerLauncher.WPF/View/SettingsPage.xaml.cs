using MCServerLauncher.WPF.Helpers;
using MCServerLauncher.WPF.View.Components;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.View
{
    /// <summary>
    ///     SettingsPage.xaml 的交互逻辑
    /// </summary>
    public partial class SettingsPage
    {
        public SettingsPage()
        {
            InitializeComponent();
            DataContext = this;

            InstanceCreation_MinecraftForgeInstallerSource.SettingComboBox.SelectedIndex = MinecraftForgeInstallerSource.ToList().IndexOf(BasicUtils.AppSettings.InstanceCreation.MinecraftForgeInstallerSource);
            InitDownloadSourceSelection();
            ResDownload_ActionWhenDownloadError.SettingComboBox.SelectedIndex = _actionWhenDownloadErrorList.IndexOf(BasicUtils.AppSettings.Download.ActionWhenDownloadError);
            Instance_ActionWhenDeleteConfirm.SettingComboBox.SelectedIndex = _actionWhenDeleteConfirmList.IndexOf(BasicUtils.AppSettings.Instance.ActionWhenDeleteConfirm);
            More_LauncherTheme.SettingComboBox.SelectedIndex = _themeList.IndexOf(BasicUtils.AppSettings.App.Theme);

            InstanceCreation_MinecraftJavaAutoAgreeEula.SettingSwitch.Toggled += OnMinecraftJavaAutoAcceptEulaChanged;
            InstanceCreation_MinecraftJavaAutoDisableOnlineMode.SettingSwitch.Toggled += OnMinecraftJavaAutoSwitchOnlineModeChanged;
            InstanceCreation_MinecraftBedrockAutoDisableOnlineMode.SettingSwitch.Toggled += OnMinecraftBedrockAutoSwitchOnlineModeChanged;
            InstanceCreation_MinecraftForgeInstallerSource.SettingComboBox.SelectionChanged += OnMinecraftForgeInstallerSourceSelectionChanged;

            ResDownload_DownloadThreadCnt.SettingSlider.ValueChanged += OnDownloadThreadValueChanged;
            ResDownload_ActionWhenDownloadError.SettingComboBox.SelectionChanged += OnActionWhenDownloadErrorSelectionChanged;

            Instance_ActionWhenDeleteConfirm.SettingComboBox.SelectionChanged += OnActionWhenDeleteConfirmIndexSelectionChanged;

            More_LauncherTheme.SettingComboBox.SelectionChanged += OnLauncherThemeIndexSelectionChanged;
            More_FollowStartupForLauncher.SettingSwitch.Toggled += OnFollowStartupForLauncherChanged;
            More_AutoCheckUpdateForLauncher.SettingSwitch.Toggled += OnAutoCheckUpdateForLauncherChanged;

            AboutVersionReplacer.Text = $"Developer Version {Assembly.GetExecutingAssembly().GetName().Version}";
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
                new PropertyMetadata(BasicUtils.AppSettings.InstanceCreation.MinecraftJavaAutoAcceptEula)
            );
        private void OnMinecraftJavaAutoAcceptEulaChanged(object sender, RoutedEventArgs e)
        {
            BasicUtils.SaveSetting("InstanceCreation.MinecraftJavaAutoAcceptEula", InstanceCreation_MinecraftJavaAutoAgreeEula.SettingSwitch.IsOn);
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
                new PropertyMetadata(BasicUtils.AppSettings.InstanceCreation.MinecraftJavaAutoSwitchOnlineMode)
            );
        private void OnMinecraftJavaAutoSwitchOnlineModeChanged(object sender, RoutedEventArgs e)
        {
            BasicUtils.SaveSetting("InstanceCreation.MinecraftJavaAutoSwitchOnlineMode", InstanceCreation_MinecraftJavaAutoDisableOnlineMode.SettingSwitch.IsOn);
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
                new PropertyMetadata(BasicUtils.AppSettings.InstanceCreation.MinecraftBedrockAutoSwitchOnlineMode)
            );
        private void OnMinecraftBedrockAutoSwitchOnlineModeChanged(object sender, RoutedEventArgs e)
        {
            BasicUtils.SaveSetting("InstanceCreation.MinecraftBedrockAutoSwitchOnlineMode", InstanceCreation_MinecraftBedrockAutoDisableOnlineMode.SettingSwitch.IsOn);
        }
        # endregion

        # region MinecraftForgeInstallerSource
        public static IEnumerable<string> MinecraftForgeInstallerSource { get; } = new List<string>
        {
            "BMCLAPI",
            "Mojang/Microsoft"
        };
        public int MinecraftForgeInstallerSourceIndex
        {
            get => (int)GetValue(MinecraftForgeInstallerSourceIndexProperty);
            set => SetValue(MinecraftForgeInstallerSourceIndexProperty, value);
        }
        public static readonly DependencyProperty MinecraftForgeInstallerSourceIndexProperty =
            DependencyProperty.Register(
                "MinecraftForgeInstallerSourceIndex",
                typeof(int),
                typeof(ComboSettingCard),
                new PropertyMetadata(MinecraftForgeInstallerSource.ToList().IndexOf(BasicUtils.AppSettings.InstanceCreation.MinecraftForgeInstallerSource))
            );
        private void OnMinecraftForgeInstallerSourceSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BasicUtils.SaveSetting("InstanceCreation.MinecraftForgeInstallerSource", InstanceCreation_MinecraftForgeInstallerSource.SettingComboBox.SelectedValue.ToString());
        }
        # endregion

        # region ResDownloadSource

        private void InitDownloadSourceSelection()
        {
            FastMirrorSrc.IsChecked = BasicUtils.AppSettings.Download.DownloadSource == "FastMirror";
            PolarsMirrorSrc.IsChecked = BasicUtils.AppSettings.Download.DownloadSource == "PolarsMirror";
            ZCloudFileSrc.IsChecked = BasicUtils.AppSettings.Download.DownloadSource == "ZCloudFile";
            MSLAPISrc.IsChecked = BasicUtils.AppSettings.Download.DownloadSource == "MSLAPI";
            MCSLSyncSrc.IsChecked = BasicUtils.AppSettings.Download.DownloadSource == "MCSLSync";
        }
        private void OnResDownloadSourceSelectionChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                BasicUtils.SaveSetting("ResDownload.DownloadSource", ((RadioButton)sender).GetType().GetProperty("Name")?.GetValue(sender).ToString().Replace("Src", ""));
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
                new PropertyMetadata(BasicUtils.AppSettings.Download.ThreadCnt)
            );
        private void OnDownloadThreadValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            BasicUtils.SaveSetting("ResDownload.ThreadCnt", (int)ResDownload_DownloadThreadCnt.SettingSlider.Value);
        }
        #endregion

        #region ActionWhenDownloadErrorIndex
        private static readonly List<string> _actionWhenDownloadErrorList = new() { "stop", "retry1", "retry3" };
        public static IEnumerable<string> ActionWhenDownloadError { get; } = new List<string>
        {
            "直接停止下载",
            "重试下载 1 次",
            "重试下载 3 次"
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
                new PropertyMetadata(_actionWhenDownloadErrorList.IndexOf(BasicUtils.AppSettings.Download.ActionWhenDownloadError))
            );
        private void OnActionWhenDownloadErrorSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                BasicUtils.SaveSetting("ResDownload.ActionWhenDownloadError", _actionWhenDownloadErrorList[ResDownload_ActionWhenDownloadError.SettingComboBox.SelectedIndex]);
            }
            catch (ArgumentOutOfRangeException)
            {
                // ignored due to mtfk error
            }
        }
        #endregion

        #region ActionWhenDeleteConfirm
        public static IEnumerable<string> ActionWhenDeleteConfirm { get; } = new List<string>
        {
            "实例名称输入验证",
            "守护进程密钥验证"
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
                new PropertyMetadata(_actionWhenDeleteConfirmList.IndexOf(BasicUtils.AppSettings.Instance.ActionWhenDeleteConfirm))
            );
        private void OnActionWhenDeleteConfirmIndexSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                BasicUtils.SaveSetting("Instance.ActionWhenDeleteConfirm", _actionWhenDeleteConfirmList[Instance_ActionWhenDeleteConfirm.SettingComboBox.SelectedIndex]);
            }
            catch (ArgumentOutOfRangeException)
            {
                // ignored due to mtfk error
            }
        }
        #endregion

        #region LauncherTheme
        public static IEnumerable<string> ThemeForApp { get; } = new List<string>
        {
            "跟随系统",
            "浅色",
            "深色"
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
                new PropertyMetadata(_themeList.IndexOf(BasicUtils.AppSettings.App.Theme))
            );
        private void OnLauncherThemeIndexSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                BasicUtils.SaveSetting("App.Theme", _themeList[More_LauncherTheme.SettingComboBox.SelectedIndex]);
            }
            catch (ArgumentOutOfRangeException)
            {
                // ignored due to mtfk error
            }
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
                new PropertyMetadata(BasicUtils.AppSettings.App.FollowStartup)
            );
        private void OnFollowStartupForLauncherChanged(object sender, RoutedEventArgs e)
        {
            BasicUtils.SaveSetting("App.FollowStartup", More_FollowStartupForLauncher.SettingSwitch.IsOn);
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
                new PropertyMetadata(BasicUtils.AppSettings.App.AutoCheckUpdate)
            );
        private void OnAutoCheckUpdateForLauncherChanged(object sender, RoutedEventArgs e)
        {
            BasicUtils.SaveSetting("App.AutoCheckUpdate", More_AutoCheckUpdateForLauncher.SettingSwitch.IsOn);
        }
        # endregion

    }

}