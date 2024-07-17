using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MCServerLauncher.UI.Helpers;

namespace MCServerLauncher.UI.View
{
    /// <summary>
    /// SettingsPage.xaml 的交互逻辑
    /// </summary>
    public partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            InitializeComponent();
            DataContext = this;

            // Will be binded in the future
            InstanceCreation_MinecraftForgeInstallerSource.SettingComboBox.SelectedIndex = 0;
            ResDownload_ActionWhenDownloadError.SettingComboBox.SelectedIndex = 0;
            InstanceManage_ActionWhenDeleteConfirm.SettingComboBox.SelectedIndex = 0;
            More_LauncherTheme.SettingComboBox.SelectedIndex = 0;

            AboutVersionReplacer.Text = $"Developer Version {Assembly.GetExecutingAssembly().GetName().Version.ToString()}";
        }
        public IEnumerable<string> MinecraftForgeInstallerSource { get; } = new List<string>
        {
            "BMCLAPI",
            "Mojang/Microsoft",
        };
        public IEnumerable<string> ActionWhenDownloadError { get; } = new List<string>
        {
            "直接停止下载",
            "重试下载 1 次",
            "重试下载 3 次"
        };
        public IEnumerable<string> ActionWhenDeleteConfirm { get; } = new List<string>
        {
            "实例名称输入验证",
            "守护进程密钥验证"
        };
        public IEnumerable<string> ThemeForApp { get; } = new List<string>
        {
            "跟随系统",
            "浅色",
            "深色"
        };
    }
}
