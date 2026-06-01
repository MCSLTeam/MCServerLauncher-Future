using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using iNKORE.UI.WPF.Modern;
using MCServerLauncher.WPF.Modules;

namespace MCServerLauncher.WPF.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private static readonly List<string?> ActionOnDoubleClickKeys = ["Console", "Start", "Stop", "Restart", "Kill"];
    private static readonly List<string?> ActionWhenDownloadErrorKeys = ["stop", "retry1", "retry3"];
    private static readonly List<string?> ThemeKeys = ["auto", "light", "dark"];

    public SettingsViewModel()
    {
        LoadBuildInfo();
    }

    // Instance Creation settings
    [ObservableProperty] private bool _minecraftJavaAutoAcceptEula = SettingsManager.Get.InstanceCreation.MinecraftJavaAutoAcceptEula;
    [ObservableProperty] private bool _minecraftJavaAutoSwitchOnlineMode = SettingsManager.Get.InstanceCreation.MinecraftJavaAutoSwitchOnlineMode;
    [ObservableProperty] private bool _minecraftBedrockAutoSwitchOnlineMode = SettingsManager.Get.InstanceCreation.MinecraftBedrockAutoSwitchOnlineMode;
    [ObservableProperty] private bool _useMirrorForMinecraftForgeInstall = SettingsManager.Get.InstanceCreation.UseMirrorForMinecraftForgeInstall;
    [ObservableProperty] private bool _useMirrorForMinecraftNeoForgeInstall = SettingsManager.Get.InstanceCreation.UseMirrorForMinecraftNeoForgeInstall;
    [ObservableProperty] private bool _useMirrorForMinecraftFabricInstall = SettingsManager.Get.InstanceCreation.UseMirrorForMinecraftFabricInstall;
    [ObservableProperty] private bool _useMirrorForMinecraftQuiltInstall = SettingsManager.Get.InstanceCreation.UseMirrorForMinecraftQuiltInstall;

    // Download settings
    [ObservableProperty] private string _downloadSource = SettingsManager.Get.Download.DownloadSource;
    [ObservableProperty] private int _downloadThreadCount = SettingsManager.Get.Download.ThreadCnt;
    [ObservableProperty] private int _actionWhenDownloadErrorIndex = ActionWhenDownloadErrorKeys.IndexOf(SettingsManager.Get.Download.ActionWhenDownloadError);

    // Instance settings
    [ObservableProperty] private int _autoRefreshInterval = SettingsManager.Get.Instance.AutoRefreshInterval;
    [ObservableProperty] private int _actionOnDoubleClickIndex = ActionOnDoubleClickKeys.IndexOf(SettingsManager.Get.Instance.ActionOnDoubleClick);

    // App settings
    [ObservableProperty] private int _launcherThemeIndex = ThemeKeys.IndexOf(SettingsManager.Get.App.Theme);
    [ObservableProperty] private int _launcherLanguageIndex = Lang.LanguageList.IndexOf(SettingsManager.Get.App.Language);
    [ObservableProperty] private bool _followStartup = SettingsManager.Get.App.FollowStartup;
    [ObservableProperty] private bool _autoCheckUpdate = SettingsManager.Get.App.AutoCheckUpdate;

    // Display info
    [ObservableProperty] private string _buildInfoText = string.Empty;
    [ObservableProperty] private string _versionText = string.Empty;

    // ComboBox item sources
    public List<string> ActionOnDoubleClickItems { get; } =
    [
        Lang.Tr["Settings_Instance_ActionOnDoubleClick_Console"],
        Lang.Tr["Settings_Instance_ActionOnDoubleClick_Start"],
        Lang.Tr["Settings_Instance_ActionOnDoubleClick_Stop"],
        Lang.Tr["Settings_Instance_ActionOnDoubleClick_Restart"],
        Lang.Tr["Settings_Instance_ActionOnDoubleClick_Kill"]
    ];

    public List<string> ActionWhenDownloadErrorItems { get; } =
    [
        Lang.Tr["Settings_ActionWhenDownloadError_Stop"],
        Lang.Tr["Settings_ActionWhenDownloadError_Retry1"],
        Lang.Tr["Settings_ActionWhenDownloadError_Retry3"]
    ];

    public List<string> ThemeItems { get; } =
    [
        Lang.Tr["Settings_AppTheme_Auto"],
        Lang.Tr["Settings_AppTheme_Light"],
        Lang.Tr["Settings_AppTheme_Dark"]
    ];

    public IEnumerable<string> LanguageItems => Lang.LanguageList;

    // Partial property change handlers for auto-save
    partial void OnMinecraftJavaAutoAcceptEulaChanged(bool value) =>
        SettingsManager.SaveSetting("InstanceCreation.MinecraftJavaAutoAcceptEula", value);

    partial void OnMinecraftJavaAutoSwitchOnlineModeChanged(bool value) =>
        SettingsManager.SaveSetting("InstanceCreation.MinecraftJavaAutoSwitchOnlineMode", value);

    partial void OnMinecraftBedrockAutoSwitchOnlineModeChanged(bool value) =>
        SettingsManager.SaveSetting("InstanceCreation.MinecraftBedrockAutoSwitchOnlineMode", value);

    partial void OnUseMirrorForMinecraftForgeInstallChanged(bool value) =>
        SettingsManager.SaveSetting("InstanceCreation.UseMirrorForMinecraftForgeInstall", value);

    partial void OnUseMirrorForMinecraftNeoForgeInstallChanged(bool value) =>
        SettingsManager.SaveSetting("InstanceCreation.UseMirrorForMinecraftNeoForgeInstall", value);

    partial void OnUseMirrorForMinecraftFabricInstallChanged(bool value) =>
        SettingsManager.SaveSetting("InstanceCreation.UseMirrorForMinecraftFabricInstall", value);

    partial void OnUseMirrorForMinecraftQuiltInstallChanged(bool value) =>
        SettingsManager.SaveSetting("InstanceCreation.UseMirrorForMinecraftQuiltInstall", value);

    partial void OnDownloadSourceChanged(string value) =>
        SettingsManager.SaveSetting("ResDownload.DownloadSource", value);

    partial void OnDownloadThreadCountChanged(int value) =>
        SettingsManager.SaveSetting("ResDownload.ThreadCnt", value);

    partial void OnActionWhenDownloadErrorIndexChanged(int value)
    {
        if (value >= 0 && value < ActionWhenDownloadErrorKeys.Count)
            SettingsManager.SaveSetting("ResDownload.ActionWhenDownloadError", ActionWhenDownloadErrorKeys[value]);
    }

    partial void OnAutoRefreshIntervalChanged(int value) =>
        SettingsManager.SaveSetting("Instance.AutoRefreshInterval", value);

    partial void OnActionOnDoubleClickIndexChanged(int value)
    {
        if (value >= 0 && value < ActionOnDoubleClickKeys.Count)
            SettingsManager.SaveSetting("Instance.ActionOnDoubleClick", ActionOnDoubleClickKeys[value]);
    }

    partial void OnLauncherThemeIndexChanged(int value)
    {
        if (value >= 0 && value < ThemeKeys.Count)
        {
            SettingsManager.SaveSetting("App.Theme", ThemeKeys[value]);
            ThemeManager.Current.ApplicationTheme = value switch
            {
                0 => null,
                1 => ApplicationTheme.Light,
                2 => ApplicationTheme.Dark,
                _ => ThemeManager.Current.ApplicationTheme
            };
        }
    }

    partial void OnLauncherLanguageIndexChanged(int value)
    {
        if (!SettingsManager.Get.App.IsFirstSetupFinished) return;
        var lang = Lang.LanguageList.ElementAt(value);
        Lang.Tr.ChangeLanguage(new CultureInfo(lang));
        SettingsManager.SaveSetting("App.Language", lang);
        RefreshLocalizedItems();
    }

    partial void OnFollowStartupChanged(bool value) =>
        SettingsManager.SaveSetting("App.FollowStartup", value);

    partial void OnAutoCheckUpdateChanged(bool value) =>
        SettingsManager.SaveSetting("App.AutoCheckUpdate", value);

    private void RefreshLocalizedItems()
    {
        ActionOnDoubleClickItems.Clear();
        ActionOnDoubleClickItems.AddRange([
            Lang.Tr["Settings_Instance_ActionOnDoubleClick_Console"],
            Lang.Tr["Settings_Instance_ActionOnDoubleClick_Start"],
            Lang.Tr["Settings_Instance_ActionOnDoubleClick_Stop"],
            Lang.Tr["Settings_Instance_ActionOnDoubleClick_Restart"],
            Lang.Tr["Settings_Instance_ActionOnDoubleClick_Kill"]
        ]);

        ActionWhenDownloadErrorItems.Clear();
        ActionWhenDownloadErrorItems.AddRange([
            Lang.Tr["Settings_ActionWhenDownloadError_Stop"],
            Lang.Tr["Settings_ActionWhenDownloadError_Retry1"],
            Lang.Tr["Settings_ActionWhenDownloadError_Retry3"]
        ]);

        ThemeItems.Clear();
        ThemeItems.AddRange([
            Lang.Tr["Settings_AppTheme_Auto"],
            Lang.Tr["Settings_AppTheme_Light"],
            Lang.Tr["Settings_AppTheme_Dark"]
        ]);

        OnPropertyChanged(nameof(ActionOnDoubleClickItems));
        OnPropertyChanged(nameof(ActionWhenDownloadErrorItems));
        OnPropertyChanged(nameof(ThemeItems));
    }

    private void LoadBuildInfo()
    {
        VersionText = $"v{Assembly.GetExecutingAssembly().GetName().Version}-REL";
#if DEBUG
        VersionText = $"v{Assembly.GetExecutingAssembly().GetName().Version}-DBG";
#endif

        var buildInfo = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("MCServerLauncher.WPF.Resources.BuildInfo");
        if (buildInfo != null)
        {
            using var reader = new System.IO.StreamReader(buildInfo);
            var buildInfoJson = reader.ReadToEnd();
            try
            {
                var obj = JsonSerializer.Deserialize<BuildInfoModel>(buildInfoJson);
                if (obj != null)
                    BuildInfoText = $"Build Time: {obj.BuildTime}\nBuild Info: {obj.Branch}-{Assembly.GetExecutingAssembly().GetName().Version}-{obj.CommitHash}";
            }
            catch
            {
                BuildInfoText = buildInfoJson;
            }
        }
    }

    private sealed class BuildInfoModel
    {
        [JsonPropertyName("buildTime")] public string? BuildTime { get; set; }
        [JsonPropertyName("commitHash")] public string? CommitHash { get; set; }
        [JsonPropertyName("branch")] public string? Branch { get; set; }
    }
}
