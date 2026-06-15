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
    private const string DefaultDownloadSource = "FastMirror";
    private const string DefaultActionWhenDownloadError = "stop";
    private const string DefaultActionOnDoubleClick = "Console";
    private const string DefaultTheme = "auto";
    private const string DefaultLanguage = "zh-CN";

    private static readonly List<string> ActionOnDoubleClickKeys = ["Console", "Start", "Stop", "Restart", "Kill"];
    private static readonly List<string> ActionWhenDownloadErrorKeys = ["stop", "retry1", "retry3"];
    private static readonly List<string> ThemeKeys = ["auto", "light", "dark"];
    private static readonly List<string> LanguageKeys = Lang.LanguageList
        .Where(language => !string.IsNullOrWhiteSpace(language))
        .Select(language => language!)
        .ToList();

    public SettingsViewModel()
    {
        LoadBuildInfo();
    }

    // Instance Creation settings
    [ObservableProperty] private bool _minecraftJavaAutoAcceptEula = InstanceCreationSettings.MinecraftJavaAutoAcceptEula;
    [ObservableProperty] private bool _minecraftJavaAutoSwitchOnlineMode = InstanceCreationSettings.MinecraftJavaAutoSwitchOnlineMode;
    [ObservableProperty] private bool _minecraftBedrockAutoSwitchOnlineMode = InstanceCreationSettings.MinecraftBedrockAutoSwitchOnlineMode;
    [ObservableProperty] private bool _useMirrorForMinecraftForgeInstall = InstanceCreationSettings.UseMirrorForMinecraftForgeInstall;
    [ObservableProperty] private bool _useMirrorForMinecraftNeoForgeInstall = InstanceCreationSettings.UseMirrorForMinecraftNeoForgeInstall;
    [ObservableProperty] private bool _useMirrorForMinecraftFabricInstall = InstanceCreationSettings.UseMirrorForMinecraftFabricInstall;
    [ObservableProperty] private bool _useMirrorForMinecraftQuiltInstall = InstanceCreationSettings.UseMirrorForMinecraftQuiltInstall;

    // Download settings
    [ObservableProperty] private string _downloadSource = DownloadSettings.DownloadSource ?? DefaultDownloadSource;
    [ObservableProperty] private int _downloadThreadCount = DownloadSettings.ThreadCnt;
    [ObservableProperty] private int _actionWhenDownloadErrorIndex = GetSelectedIndex(ActionWhenDownloadErrorKeys, DownloadSettings.ActionWhenDownloadError, DefaultActionWhenDownloadError);

    // Instance settings
    [ObservableProperty] private int _autoRefreshInterval = InstanceSettings.AutoRefreshInterval;
    [ObservableProperty] private int _actionOnDoubleClickIndex = GetSelectedIndex(ActionOnDoubleClickKeys, InstanceSettings.ActionOnDoubleClick, DefaultActionOnDoubleClick);

    // App settings
    [ObservableProperty] private int _launcherThemeIndex = GetSelectedIndex(ThemeKeys, AppSettings.Theme, DefaultTheme);
    [ObservableProperty] private int _launcherLanguageIndex = GetSelectedIndex(LanguageKeys, AppSettings.Language, DefaultLanguage);
    [ObservableProperty] private bool _followStartup = AppSettings.FollowStartup;
    [ObservableProperty] private bool _autoCheckUpdate = AppSettings.AutoCheckUpdate;

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

    public IEnumerable<string> LanguageItems => LanguageKeys;

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
        if (!AppSettings.IsFirstSetupFinished) return;
        if (value < 0 || value >= LanguageKeys.Count) return;

        var lang = LanguageKeys[value];
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

    private static SettingsManager.Settings EnsureSettings()
    {
        var settings = SettingsManager.Get ??= new SettingsManager.Settings();
        return settings;
    }

    private static SettingsManager.InstanceCreationSettingsModel InstanceCreationSettings =>
        EnsureSettings().InstanceCreation ??= CreateDefaultInstanceCreationSettings();

    private static SettingsManager.ResDownloadSettingsModel DownloadSettings =>
        EnsureSettings().Download ??= CreateDefaultDownloadSettings();

    private static SettingsManager.InstanceSettingsModel InstanceSettings =>
        EnsureSettings().Instance ??= new SettingsManager.InstanceSettingsModel();

    private static SettingsManager.AppSettingsModel AppSettings =>
        EnsureSettings().App ??= CreateDefaultAppSettings();

    private static int GetSelectedIndex(IReadOnlyList<string> values, string? selectedValue, string defaultValue)
    {
        var index = selectedValue is null ? -1 : IndexOf(values, selectedValue);
        return index >= 0 ? index : IndexOf(values, defaultValue);
    }

    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], value, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static SettingsManager.InstanceCreationSettingsModel CreateDefaultInstanceCreationSettings()
    {
        return new SettingsManager.InstanceCreationSettingsModel
        {
            MinecraftJavaAutoAcceptEula = false,
            MinecraftJavaAutoSwitchOnlineMode = false,
            MinecraftBedrockAutoSwitchOnlineMode = false,
            UseMirrorForMinecraftForgeInstall = true,
            UseMirrorForMinecraftNeoForgeInstall = true,
            UseMirrorForMinecraftFabricInstall = true,
            UseMirrorForMinecraftQuiltInstall = true
        };
    }

    private static SettingsManager.ResDownloadSettingsModel CreateDefaultDownloadSettings()
    {
        return new SettingsManager.ResDownloadSettingsModel
        {
            DownloadSource = DefaultDownloadSource,
            ThreadCnt = 16,
            ActionWhenDownloadError = DefaultActionWhenDownloadError
        };
    }

    private static SettingsManager.AppSettingsModel CreateDefaultAppSettings()
    {
        return new SettingsManager.AppSettingsModel
        {
            Theme = DefaultTheme,
            Language = DefaultLanguage,
            FollowStartup = false,
            AutoCheckUpdate = true,
            IsCertImported = false,
            IsFontInstalled = false,
            IsAppEulaAccepted = false,
            IsFirstSetupFinished = false
        };
    }

    private sealed class BuildInfoModel
    {
        [JsonPropertyName("buildTime")] public string? BuildTime { get; set; }
        [JsonPropertyName("commitHash")] public string? CommitHash { get; set; }
        [JsonPropertyName("branch")] public string? Branch { get; set; }
    }
}
