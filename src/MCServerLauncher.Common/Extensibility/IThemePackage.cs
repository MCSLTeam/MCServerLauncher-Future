using System.Collections.Generic;

namespace MCServerLauncher.Common.Extensibility;

public interface IThemePackage
{
    string Id { get; }
    string DisplayName { get; }
    string Author { get; }
    ThemeMode BaseTheme { get; }
    uint AccentColor { get; }
    uint? BackgroundColor { get; }
    IReadOnlyDictionary<string, string> CustomResources { get; }
}
