using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace MCServerLauncher.WPF.View.Pages
{
    public partial class SettingsPage
    {
        private readonly SettingsViewModel _viewModel;
        private int _debugClickCount;

        public SettingsPage()
        {
            InitializeComponent();
            _viewModel = App.ViewModelLocator.Settings;
            DataContext = _viewModel;

            InitializeControls();
            BindEventHandlers();
        }

        private void InitializeControls()
        {
            InitDownloadSourceSelection();
            ResDownload_DownloadThreadCnt.SettingSlider.Value = _viewModel.DownloadThreadCount;
            ResDownload_ActionWhenDownloadError.SettingComboBox.SelectedIndex = _viewModel.ActionWhenDownloadErrorIndex;
            Instance_AutoRefreshInterval.SettingSlider.Value = _viewModel.AutoRefreshInterval;
            Instance_ActionOnDoubleClick.SettingComboBox.SelectedIndex = _viewModel.ActionOnDoubleClickIndex;
            More_LauncherTheme.SettingComboBox.SelectedIndex = _viewModel.LauncherThemeIndex;
            More_LauncherLanguage.SettingComboBox.SelectedIndex = _viewModel.LauncherLanguageIndex;

            BuildInfoReplacer.Text = _viewModel.BuildInfoText;
            AboutVersionReplacer.Text = _viewModel.VersionText;
        }

        private void BindEventHandlers()
        {
            InstanceCreation_MinecraftJavaAutoAgreeEula.SettingSwitch.Toggled += (s, e) =>
                _viewModel.MinecraftJavaAutoAcceptEula = InstanceCreation_MinecraftJavaAutoAgreeEula.SettingSwitch.IsOn;
            InstanceCreation_MinecraftJavaAutoDisableOnlineMode.SettingSwitch.Toggled += (s, e) =>
                _viewModel.MinecraftJavaAutoSwitchOnlineMode = InstanceCreation_MinecraftJavaAutoDisableOnlineMode.SettingSwitch.IsOn;
            InstanceCreation_MinecraftBedrockAutoDisableOnlineMode.SettingSwitch.Toggled += (s, e) =>
                _viewModel.MinecraftBedrockAutoSwitchOnlineMode = InstanceCreation_MinecraftBedrockAutoDisableOnlineMode.SettingSwitch.IsOn;
            InstanceCreation_UseMirrorForMinecraftForgeInstall.SettingSwitch.Toggled += (s, e) =>
                _viewModel.UseMirrorForMinecraftForgeInstall = InstanceCreation_UseMirrorForMinecraftForgeInstall.SettingSwitch.IsOn;
            InstanceCreation_UseMirrorForMinecraftNeoForgeInstall.SettingSwitch.Toggled += (s, e) =>
                _viewModel.UseMirrorForMinecraftNeoForgeInstall = InstanceCreation_UseMirrorForMinecraftNeoForgeInstall.SettingSwitch.IsOn;
            InstanceCreation_UseMirrorForMinecraftFabricInstall.SettingSwitch.Toggled += (s, e) =>
                _viewModel.UseMirrorForMinecraftFabricInstall = InstanceCreation_UseMirrorForMinecraftFabricInstall.SettingSwitch.IsOn;
            InstanceCreation_UseMirrorForMinecraftQuiltInstall.SettingSwitch.Toggled += (s, e) =>
                _viewModel.UseMirrorForMinecraftQuiltInstall = InstanceCreation_UseMirrorForMinecraftQuiltInstall.SettingSwitch.IsOn;

            ResDownload_DownloadThreadCnt.SettingSlider.ValueChanged += (s, e) =>
                _viewModel.DownloadThreadCount = (int)ResDownload_DownloadThreadCnt.SettingSlider.Value;
            ResDownload_ActionWhenDownloadError.SettingComboBox.SelectionChanged += (s, e) =>
                _viewModel.ActionWhenDownloadErrorIndex = ResDownload_ActionWhenDownloadError.SettingComboBox.SelectedIndex;

            Instance_AutoRefreshInterval.SettingSlider.ValueChanged += (s, e) =>
                _viewModel.AutoRefreshInterval = (int)Instance_AutoRefreshInterval.SettingSlider.Value;
            Instance_ActionOnDoubleClick.SettingComboBox.SelectionChanged += (s, e) =>
                _viewModel.ActionOnDoubleClickIndex = Instance_ActionOnDoubleClick.SettingComboBox.SelectedIndex;

            More_LauncherTheme.SettingComboBox.SelectionChanged += (s, e) =>
                _viewModel.LauncherThemeIndex = More_LauncherTheme.SettingComboBox.SelectedIndex;
            More_LauncherLanguage.SettingComboBox.SelectionChanged += (s, e) =>
                _viewModel.LauncherLanguageIndex = More_LauncherLanguage.SettingComboBox.SelectedIndex;
            More_FollowStartupForLauncher.SettingSwitch.Toggled += (s, e) =>
                _viewModel.FollowStartup = More_FollowStartupForLauncher.SettingSwitch.IsOn;
            More_AutoCheckUpdateForLauncher.SettingSwitch.Toggled += (s, e) =>
                _viewModel.AutoCheckUpdate = More_AutoCheckUpdateForLauncher.SettingSwitch.IsOn;
        }

        private void InitDownloadSourceSelection()
        {
            FastMirrorSrc.IsChecked = _viewModel.DownloadSource == "FastMirror";
            PolarsMirrorSrc.IsChecked = _viewModel.DownloadSource == "PolarsMirror";
            RainYunSrc.IsChecked = _viewModel.DownloadSource == "RainYun";
            MSLAPISrc.IsChecked = _viewModel.DownloadSource == "MSLAPI";
            MCSLSyncSrc.IsChecked = _viewModel.DownloadSource == "MCSLSync";
        }

        private void OnResDownloadSourceSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton rb)
            {
                _viewModel.DownloadSource = rb.Name.Replace("Src", "");
            }
        }

        private void CheckDebugMode(object sender, MouseButtonEventArgs e)
        {
            _debugClickCount += 1;
            if (_debugClickCount >= 5)
            {
                var parent = this.TryFindParent<MainWindow>();
                if (parent != null) parent.DebugItem.Visibility = Visibility.Visible;
            }
        }
    }
}
