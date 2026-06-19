using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using iNKORE.UI.WPF.Modern;
using MCServerLauncher.Common.Extensibility;
using Serilog;

namespace MCServerLauncher.WPF.Services;

public class ThemeService : IThemeService
{
    private readonly List<IThemePackage> _themes = [];

    public IReadOnlyList<IThemePackage> AvailableThemes => _themes;
    public IThemePackage? CurrentTheme { get; private set; }

    public ThemeService()
    {
        _themes.Add(new BuiltInThemePackage("auto", "Auto", ThemeMode.Auto, 0xFF0078D4));
        _themes.Add(new BuiltInThemePackage("light", "Light", ThemeMode.Light, 0xFF0078D4));
        _themes.Add(new BuiltInThemePackage("dark", "Dark", ThemeMode.Dark, 0xFF0078D4));
    }

    public void ApplyTheme(IThemePackage theme)
    {
        CurrentTheme = theme;

        ThemeManager.Current.ApplicationTheme = theme.BaseTheme switch
        {
            ThemeMode.Light => ApplicationTheme.Light,
            ThemeMode.Dark => ApplicationTheme.Dark,
            _ => null
        };

        var accentColor = Color.FromArgb(
            (byte)(theme.AccentColor >> 24),
            (byte)(theme.AccentColor >> 16),
            (byte)(theme.AccentColor >> 8),
            (byte)theme.AccentColor);
        ThemeManager.Current.AccentColor = accentColor;

        Log.Information("[Theme] Applied theme: {0} (accent: #{1:X8})", theme.DisplayName, theme.AccentColor);
    }

    public void LoadThemesFromDirectory(string path)
    {
        if (!Directory.Exists(path)) return;

        foreach (var file in Directory.GetFiles(path, "*.mtheme"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var package = JsonSerializer.Deserialize<ThemePackageFile>(json);
                if (package != null)
                {
                    _themes.Add(package);
                    Log.Information("[Theme] Loaded theme: {0} from {1}", package.DisplayName, file);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Theme] Failed to load theme from {0}", file);
            }
        }
    }

    private sealed class BuiltInThemePackage : IThemePackage
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Author => "MCSLTeam";
        public ThemeMode BaseTheme { get; }
        public uint AccentColor { get; }
        public uint? BackgroundColor => null;
        public IReadOnlyDictionary<string, string> CustomResources { get; } = new Dictionary<string, string>();

        public BuiltInThemePackage(string id, string displayName, ThemeMode baseTheme, uint accentColor)
        {
            Id = id;
            DisplayName = displayName;
            BaseTheme = baseTheme;
            AccentColor = accentColor;
        }
    }

    private sealed class ThemePackageFile : IThemePackage
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
        [JsonPropertyName("author")] public string Author { get; set; } = "";
        [JsonPropertyName("baseTheme")] public ThemeMode BaseTheme { get; set; }
        [JsonPropertyName("accentColor")] public uint AccentColor { get; set; } = 0xFF0078D4;
        [JsonPropertyName("backgroundColor")] public uint? BackgroundColor { get; set; }
        [JsonPropertyName("customResources")] public Dictionary<string, string> Resources { get; set; } = new();

        IReadOnlyDictionary<string, string> IThemePackage.CustomResources => Resources;
    }
}
