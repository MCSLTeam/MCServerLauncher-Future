using MCServerLauncher.WPF.Helpers;
using MCServerLauncher.WPF.View.Components;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using iNKORE.UI.WPF.Modern.Controls;

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

            InstanceCreation_MinecraftJavaAutoAgreeEula.SettingSwitch.Toggled += OnMinecraftJavaAutoAcceptEulaChanged;
            InstanceCreation_MinecraftJavaAutoDisableOnlineMode.SettingSwitch.Toggled += OnMinecraftJavaAutoSwitchOnlineModeChanged;
            InstanceCreation_MinecraftBedrockAutoDisableOnlineMode.SettingSwitch.Toggled += OnMinecraftBedrockAutoSwitchOnlineModeChanged;
            InstanceCreation_MinecraftForgeInstallerSource.SettingComboBox.SelectionChanged += OnMinecraftForgeInstallerSourceSelectionChanged;

            ResDownload_DownloadSource.SelectionChanged += OnResDownloadSourceSelectionChanged;
            ResDownload_DownloadThread.SettingSlider.ValueChanged += OnDownloadThreadValueChanged;
            ResDownload_ActionWhenDownloadError.SettingComboBox.SelectionChanged += OnActionWhenDownloadErrorSelectionChanged;

            Instance_ActionWhenDeleteConfirm.SettingComboBox.SelectionChanged += OnActionWhenDeleteConfirmIndexSelectionChanged;

            InstanceCreation_MinecraftForgeInstallerSource.SettingComboBox.SelectedIndex = MinecraftForgeInstallerSource.ToList().IndexOf(BasicUtils.AppSettings.InstanceCreation.MinecraftForgeInstallerSource);
            ResDownload_DownloadSource.SelectedIndex = _downloadSourceList.IndexOf(BasicUtils.AppSettings.Download.DownloadSource);
            ResDownload_ActionWhenDownloadError.SettingComboBox.SelectedIndex = _actionWhenDownloadErrorList.IndexOf(BasicUtils.AppSettings.Download.ActionWhenDownloadError);
            Instance_ActionWhenDeleteConfirm.SettingComboBox.SelectedIndex = _actionWhenDeleteConfirmList.IndexOf(BasicUtils.AppSettings.Instance.ActionWhenDeleteConfirm);
            More_LauncherTheme.SettingComboBox.SelectedIndex = 0;
            AboutVersionReplacer.Text = $"Developer Version {Assembly.GetExecutingAssembly().GetName().Version}";
        }

        public static IEnumerable<string> ThemeForApp { get; } = new List<string>
        {
            "跟随系统",
            "浅色",
            "深色"
        };

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
        private static readonly List<string> _downloadSourceList = new() { "FastMirror", "PolarsMirror", "ZCloudFile", "MSLAPI", "MCSL-Sync" };
        private void OnResDownloadSourceSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Console.WriteLine(11);
            Console.WriteLine(_downloadSourceList[((RadioButtons)sender).SelectedIndex]);
            try
            {
                BasicUtils.SaveSetting("ResDownload.DownloadSource", _downloadSourceList[((RadioButtons)sender).SelectedIndex]);
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
                new PropertyMetadata(BasicUtils.AppSettings.Download.Thread)
            );
        private void OnDownloadThreadValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            BasicUtils.SaveSetting("ResDownload.Thread", (int)ResDownload_DownloadThread.SettingSlider.Value);
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
                new PropertyMetadata(ActionWhenDownloadError.ToList().IndexOf(BasicUtils.AppSettings.Download.ActionWhenDownloadError))
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
            Console.WriteLine(1232132);
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

    }

}