using System.Collections.Generic;
using MCServerLauncher.Common.Extensibility;

namespace MCServerLauncher.WPF.Services;

public interface IThemeService
{
    IReadOnlyList<IThemePackage> AvailableThemes { get; }
    IThemePackage? CurrentTheme { get; }
    void ApplyTheme(IThemePackage theme);
    void LoadThemesFromDirectory(string path);
}
