namespace MCServerLauncher.Common.ProtoType.Instance;

/// <summary>
///     实例安装设置: 源文件类型
/// </summary>
public enum SourceType
{
    /// <summary>
    ///     仅初始化, 非法值
    /// </summary>
    None,

    /// <summary>
    ///     压缩包(zip)(解压后直接创建)
    /// </summary>
    Archive,

    /// <summary>
    ///     核心文件(按照配置进行目标文件安装)
    /// </summary>
    Core,

    /// <summary>
    ///     脚本文件(运行脚本安装)
    /// </summary>
    Script
}