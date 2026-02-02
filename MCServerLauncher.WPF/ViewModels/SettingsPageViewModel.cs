using CommunityToolkit.Mvvm.ComponentModel;
using iNKORE.UI.WPF.Modern;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services.Interfaces;
using MCServerLauncher.WPF.ViewModels.Base;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace MCServerLauncher.WPF.ViewModels
{
    /// <summary>
    /// ViewModel for the SettingsPage.
    /// </summary>
    public partial class SettingsPageViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;

        // Instance Creation Settings
        [ObservableProperty]
        private bool _minecraftJavaAutoAcceptEula;

        [ObservableProperty]
        private bool _minecraftJavaAutoSwitchOnlineMode;

        [ObservableProperty]
        private bool _minecraftBedrockAutoSwitchOnlineMode;

        [ObservableProperty]
        private bool _useMirrorForMinecraftForgeInstall;

        [ObservableProperty]
        private bool _useMirrorForMinecraftNeoForgeInstall;

        [ObservableProperty]
        private bool _useMirrorForMinecraftFabricInstall;

        [ObservableProperty]
        private bool _useMirrorForMinecraftQuiltInstall;

        // Download Settings
        [ObservableProperty]
        private string _downloadSource = "FastMirror";

        [ObservableProperty]
        private int _downloadThreadCount;

        [ObservableProperty]
        private int _actionWhenDownloadErrorIndex;

        // Instance Settings
        [ObservableProperty]
        private int _actionWhenDeleteConfirmIndex;

        // App Settings
        [ObservableProperty]
        private int _themeIndex;

        [ObservableProperty]
        private int _languageIndex;

        [ObservableProperty]
        private bool _followStartup;

        [ObservableProperty]
        private bool _autoCheckUpdate;

        // ComboBox Options
        public List<string> ActionWhenDownloadErrorList { get; set; }
        public List<string> ActionWhenDeleteConfirmList { get; set; }
        public List<string> ThemeList { get; set; }
        public List<string> LanguageList { get; set; }

        // Download Source Options
        [ObservableProperty]
        private bool _isFastMirrorChecked;

        [ObservableProperty]
        private bool _isPolarsMirrorChecked;

        [ObservableProperty]
        private bool _isRainYunChecked;

        [ObservableProperty]
        private bool _isMSLAPIChecked;

        [ObservableProperty]
        private bool _isMCSLSyncChecked;

        // Build Info
        [ObservableProperty]
        private string _buildInfo = string.Empty;

        [ObservableProperty]
        private string _versionInfo = string.Empty;

        public SettingsPageViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;

            // Initialize ComboBox lists
            ActionWhenDownloadErrorList = new List<string>
            {
                Lang.Tr["Settings_ActionWhenDownloadError_Stop"],
                Lang.Tr["Settings_ActionWhenDownloadError_Retry1"],
                Lang.Tr["Settings_ActionWhenDownloadError_Retry3"]
            };

            ActionWhenDeleteConfirmList = new List<string>
            {
                Lang.Tr["Settings_ActionWhenDeleteConfirm_TypeName"],
                Lang.Tr["Settings_ActionWhenDeleteConfirm_TypeKey"]
            };

            ThemeList = new List<string>
            {
                Lang.Tr["Settings_AppTheme_Auto"],
                Lang.Tr["Settings_AppTheme_Light"],
                Lang.Tr["Settings_AppTheme_Dark"]
            };

            LanguageList = Lang.LanguageList.ToList();

            LoadSettings();
            LoadBuildInfo();
        }

        private void LoadSettings()
        {
            var settings = _settingsService.CurrentSettings;

            // Instance Creation
            MinecraftJavaAutoAcceptEula = settings.InstanceCreation?.MinecraftJavaAutoAcceptEula ?? false;
            MinecraftJavaAutoSwitchOnlineMode = settings.InstanceCreation?.MinecraftJavaAutoSwitchOnlineMode ?? false;
            MinecraftBedrockAutoSwitchOnlineMode = settings.InstanceCreation?.MinecraftBedrockAutoSwitchOnlineMode ?? false;
            UseMirrorForMinecraftForgeInstall = settings.InstanceCreation?.UseMirrorForMinecraftForgeInstall ?? true;
            UseMirrorForMinecraftNeoForgeInstall = settings.InstanceCreation?.UseMirrorForMinecraftNeoForgeInstall ?? true;
            UseMirrorForMinecraftFabricInstall = settings.InstanceCreation?.UseMirrorForMinecraftFabricInstall ?? true;
            UseMirrorForMinecraftQuiltInstall = settings.InstanceCreation?.UseMirrorForMinecraftQuiltInstall ?? true;

            // Download
            DownloadSource = settings.Download?.DownloadSource ?? "FastMirror";
            UpdateDownloadSourceChecks();
            DownloadThreadCount = settings.Download?.ThreadCnt ?? 16;
            ActionWhenDownloadErrorIndex = settings.Download?.ActionWhenDownloadError switch
            {
                "stop" => 0,
                "retry1" => 1,
                "retry3" => 2,
                _ => 0
            };

            // Instance
            ActionWhenDeleteConfirmIndex = settings.Instance?.ActionWhenDeleteConfirm switch
            {
                "name" => 0,
                "key" => 1,
                _ => 0
            };

            // App
            ThemeIndex = settings.App?.Theme switch
            {
                "auto" => 0,
                "light" => 1,
                "dark" => 2,
                _ => 0
            };

            LanguageIndex = Lang.LanguageList.IndexOf(settings.App?.Language ?? "zh-CN");
            FollowStartup = settings.App?.FollowStartup ?? false;
            AutoCheckUpdate = settings.App?.AutoCheckUpdate ?? true;
        }

        private void UpdateDownloadSourceChecks()
        {
            IsFastMirrorChecked = DownloadSource == "FastMirror";
            IsPolarsMirrorChecked = DownloadSource == "PolarsMirror";
            IsRainYunChecked = DownloadSource == "RainYun";
            IsMSLAPIChecked = DownloadSource == "MSLAPI";
            IsMCSLSyncChecked = DownloadSource == "MCSLSync";
        }

        private void LoadBuildInfo()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var buildInfo = assembly.GetManifestResourceStream("MCServerLauncher.WPF.Resources.BuildInfo");
            if (buildInfo != null)
            {
                using var reader = new System.IO.StreamReader(buildInfo);
                var buildInfoJson = reader.ReadToEnd();
                try
                {
                    var buildInfoObj = System.Text.Json.JsonSerializer.Deserialize<BuildInfoModel>(buildInfoJson);
                    if (buildInfoObj != null)
                    {
                        BuildInfo = $"Build Time: {buildInfoObj.BuildTime}\nBuild Info: {buildInfoObj.Branch}-{assembly.GetName().Version}-{buildInfoObj.CommitHash}";
                    }
                }
                catch
                {
                    BuildInfo = buildInfoJson;
                }
            }

            VersionInfo = $"v{assembly.GetName().Version}-REL";
#if DEBUG
            VersionInfo = $"v{assembly.GetName().Version}-DBG";
#endif
        }

        // Property change handlers for auto-save
        partial void OnMinecraftJavaAutoAcceptEulaChanged(bool value)
        {
            _settingsService.SaveSetting("InstanceCreation.MinecraftJavaAutoAcceptEula", value);
        }

        partial void OnMinecraftJavaAutoSwitchOnlineModeChanged(bool value)
        {
            _settingsService.SaveSetting("InstanceCreation.MinecraftJavaAutoSwitchOnlineMode", value);
        }

        partial void OnMinecraftBedrockAutoSwitchOnlineModeChanged(bool value)
        {
            _settingsService.SaveSetting("InstanceCreation.MinecraftBedrockAutoSwitchOnlineMode", value);
        }

        partial void OnUseMirrorForMinecraftForgeInstallChanged(bool value)
        {
            _settingsService.SaveSetting("InstanceCreation.UseMirrorForMinecraftForgeInstall", value);
        }

        partial void OnUseMirrorForMinecraftNeoForgeInstallChanged(bool value)
        {
            _settingsService.SaveSetting("InstanceCreation.UseMirrorForMinecraftNeoForgeInstall", value);
        }

        partial void OnUseMirrorForMinecraftFabricInstallChanged(bool value)
        {
            _settingsService.SaveSetting("InstanceCreation.UseMirrorForMinecraftFabricInstall", value);
        }

        partial void OnUseMirrorForMinecraftQuiltInstallChanged(bool value)
        {
            _settingsService.SaveSetting("InstanceCreation.UseMirrorForMinecraftQuiltInstall", value);
        }

        partial void OnDownloadSourceChanged(string value)
        {
            _settingsService.SaveSetting("ResDownload.DownloadSource", value);
            UpdateDownloadSourceChecks();
        }

        partial void OnIsFastMirrorCheckedChanged(bool value)
        {
            if (value) DownloadSource = "FastMirror";
        }

        partial void OnIsPolarsMirrorCheckedChanged(bool value)
        {
            if (value) DownloadSource = "PolarsMirror";
        }

        partial void OnIsRainYunCheckedChanged(bool value)
        {
            if (value) DownloadSource = "RainYun";
        }

        partial void OnIsMSLAPICheckedChanged(bool value)
        {
            if (value) DownloadSource = "MSLAPI";
        }

        partial void OnIsMCSLSyncCheckedChanged(bool value)
        {
            if (value) DownloadSource = "MCSLSync";
        }

        partial void OnDownloadThreadCountChanged(int value)
        {
            _settingsService.SaveSetting("ResDownload.ThreadCnt", value);
        }

        partial void OnActionWhenDownloadErrorIndexChanged(int value)
        {
            var actionValue = value switch
            {
                0 => "stop",
                1 => "retry1",
                2 => "retry3",
                _ => "stop"
            };
            _settingsService.SaveSetting("ResDownload.ActionWhenDownloadError", actionValue);
        }

        partial void OnActionWhenDeleteConfirmIndexChanged(int value)
        {
            var actionValue = value switch
            {
                0 => "name",
                1 => "key",
                _ => "name"
            };
            _settingsService.SaveSetting("Instance.ActionWhenDeleteConfirm", actionValue);
        }

        partial void OnThemeIndexChanged(int value)
        {
            var theme = value switch
            {
                0 => "auto",
                1 => "light",
                2 => "dark",
                _ => "auto"
            };
            _settingsService.SaveSetting("App.Theme", theme);
            ApplyTheme(value);
        }

        partial void OnLanguageIndexChanged(int value)
        {
            if (!_settingsService.CurrentSettings.App?.IsFirstSetupFinished ?? false) return;

            var language = Lang.LanguageList.ElementAt(value);
            Lang.Tr.ChangeLanguage(new CultureInfo(language));
            _settingsService.SaveSetting("App.Language", language);
            UpdateLanguageDependentLists();
        }

        partial void OnFollowStartupChanged(bool value)
        {
            _settingsService.SaveSetting("App.FollowStartup", value);
        }

        partial void OnAutoCheckUpdateChanged(bool value)
        {
            _settingsService.SaveSetting("App.AutoCheckUpdate", value);
        }

        private void ApplyTheme(int themeIndex)
        {
            ThemeManager.Current.ApplicationTheme = themeIndex switch
            {
                0 => null,
                1 => ApplicationTheme.Light,
                2 => ApplicationTheme.Dark,
                _ => null
            };
        }

        private void UpdateLanguageDependentLists()
        {
            ActionWhenDownloadErrorList = new List<string>
            {
                Lang.Tr["Settings_ActionWhenDownloadError_Stop"],
                Lang.Tr["Settings_ActionWhenDownloadError_Retry1"],
                Lang.Tr["Settings_ActionWhenDownloadError_Retry3"]
            };

            ActionWhenDeleteConfirmList = new List<string>
            {
                Lang.Tr["Settings_ActionWhenDeleteConfirm_TypeName"],
                Lang.Tr["Settings_ActionWhenDeleteConfirm_TypeKey"]
            };

            ThemeList = new List<string>
            {
                Lang.Tr["Settings_AppTheme_Auto"],
                Lang.Tr["Settings_AppTheme_Light"],
                Lang.Tr["Settings_AppTheme_Dark"]
            };

            OnPropertyChanged(nameof(ActionWhenDownloadErrorList));
            OnPropertyChanged(nameof(ActionWhenDeleteConfirmList));
            OnPropertyChanged(nameof(ThemeList));
        }
    }

    public class BuildInfoModel
    {
        public string? BuildTime { get; set; }
        public string? Branch { get; set; }
        public string? CommitHash { get; set; }
    }
}
