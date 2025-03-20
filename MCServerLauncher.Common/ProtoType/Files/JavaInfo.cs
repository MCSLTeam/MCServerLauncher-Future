namespace MCServerLauncher.Common.ProtoType;

/// <summary>
///     改用struct: 默认实现了值比较
/// </summary>
public record struct JavaInfo(string Path, string Version, string Architecture);