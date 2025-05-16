namespace MCServerLauncher.Common.ProtoType.Instance;

/// <summary>
///     目标文件类型
/// </summary>
public enum TargetType
{
    /// <summary>
    ///     目标文件为Java Jar文件
    /// </summary>
    Jar,

    /// <summary>
    ///     目标文件为脚本文件(bat, ps1, sh, ...)
    /// </summary>
    Script,

    /// <summary>
    ///     目标文件为可执行文件
    /// </summary>
    Executable
}