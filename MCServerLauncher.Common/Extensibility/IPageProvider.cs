namespace MCServerLauncher.Common.Extensibility;

public interface IPageProvider
{
    string Id { get; }
    string DisplayName { get; }
    string IconGlyph { get; }
    int Order { get; }
    PageTarget Target { get; }
}

public enum PageTarget
{
    MainWindow,
    InstanceConsole
}
