using System.Text;
using Newtonsoft.Json;

namespace MCServerLauncher.Common.ProtoType.Instance;

public record InstanceConfig
{
    /// <summary>
    ///     配置的固定文件名
    /// </summary>
    [JsonIgnore] public const string FileName = "daemon_instance.json";

    #region Required

    /// <summary>
    ///     Minecraft版本
    /// </summary>
    [JsonProperty(Required = Required.Always)]
    public string McVersion { get; init; } = null!;

    /// <summary>
    ///     服务器名称
    /// </summary>
    [JsonProperty(Required = Required.Always)]
    public string Name { get; init; } = null!;

    /// <summary>
    ///     java虚拟机路径
    /// </summary>
    [JsonProperty(Required = Required.Always)]
    public string JavaPath { get; init; } = null!;
    
    /// <summary>
    ///     服务器启动目标(jar文件名, 脚本文件名)
    /// </summary>
    [JsonProperty(Required = Required.Always)]
    public string Target { get; init; } = null!;
    
    /// <summary>
    ///     服务器类型(vanilla, fabric, forge ...)
    /// </summary>
    [JsonProperty(Required = Required.Always)]
    public InstanceType InstanceType { get; init; }

    /// <summary>
    ///     服务器启动目标类型(jar, script[bat, sh])
    /// </summary>
    [JsonProperty(Required = Required.Always)]
    public TargetType TargetType { get; init; }

    #endregion

    #region Not Required

    /// <summary>
    ///     服务器Uuid,实例化<see cref="InstanceConfig" />会默认生成
    /// </summary>
    public Guid Uuid = Guid.NewGuid();

    /// <summary>
    ///     控制台输入编码
    /// </summary>
    [JsonConverter(typeof(WebEncodingJsonConverter))]
    public Encoding InputEncoding { get; init; } = Encoding.UTF8;
    
    /// <summary>
    ///     控制台输出编码
    /// </summary>
    [JsonConverter(typeof(WebEncodingJsonConverter))]
    public Encoding OutputEncoding { get; init; } = Encoding.UTF8;

    /// <summary>
    ///     java虚拟机参数列表
    /// </summary>
    public string[] JavaArgs { get; init; } = Array.Empty<string>();

    #endregion
}