using System.Text;
using Newtonsoft.Json;

namespace MCServerLauncher.Common.ProtoType.Instance;

public record InstanceConfig
{
    /// <summary>
    ///     配置的固定文件名
    /// </summary>
    [JsonIgnore] public const string FileName = "daemon_instance.json";

    /// <summary>
    ///     服务器Uuid,实例化<see cref="InstanceConfig" />会默认生成
    /// </summary>
    public Guid Uuid = Guid.NewGuid();

    /// <summary>
    ///     控制台输入编码
    /// </summary>
    public Encoding InputEncoding { get; set; } = Encoding.UTF8;

    /// <summary>
    ///     服务器工作目录
    /// </summary>
    public string WorkingDirectory { get; set; } = null!;

    /// <summary>
    ///     java虚拟机参数列表
    /// </summary>
    public string[] JavaArgs { get; set; } = null!;

    /// <summary>
    ///     java虚拟机路径
    /// </summary>
    public string JavaPath { get; set; } = null!;

    /// <summary>
    ///     服务器名称
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    ///     控制台输出编码
    /// </summary>
    public Encoding OutputEncoding { get; set; } = Encoding.UTF8;

    /// <summary>
    ///     服务器类型(vanilla, fabric, forge ...)
    /// </summary>
    public InstanceType InstanceType { get; set; }

    /// <summary>
    ///     服务器启动目标(jar文件名, 脚本文件名)
    /// </summary>
    public string Target { get; set; } = null!;

    /// <summary>
    ///     服务器启动目标类型(jar, script[bat, sh])
    /// </summary>
    public TargetType TargetType { get; set; }
}