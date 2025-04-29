using System.Text;
using Newtonsoft.Json;

namespace MCServerLauncher.Common.ProtoType.Instance;

/// <summary>
///  实例配置文件, 用于支持Daemon启动MC服务器, 普通Jar文件, 脚本文件, 可执行文件
/// </summary>
public record InstanceConfig
{
    /// <summary>
    ///     配置的固定文件名
    /// </summary>
    [JsonIgnore] public const string FileName = "daemon_instance.json";

    #region Required

    /// <summary>
    ///     服务器名称
    /// </summary>
    [JsonProperty(Required = Required.Always)]
    public string Name { get; init; } = null!;

    /// <summary>
    ///     服务器启动目标(jar文件名, 脚本文件名, 可执行文件名)
    /// </summary>
    [JsonProperty(Required = Required.Always)]
    public string Target { get; init; } = null!;

    /// <summary>
    ///     默认为MC服务器,实例服务器类型(none, universal, fabric, forge ...).
    ///     如果不为MC服务器, 则因置为<see cref="InstanceType.None"/>
    /// </summary>
    [JsonProperty(Required = Required.Always)]
    public InstanceType InstanceType { get; init; }

    /// <summary>
    ///     服务器启动目标类型(jar, script[bat, sh], executable)
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
    ///     Minecraft版本, 非mc服务器可以为空或者null
    /// </summary>
    public string McVersion { get; init; } = string.Empty;

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
    ///     java虚拟机路径, 非MC服务器或<see cref="InstanceConfig.TargetType"/>不为<see cref="TargetType.Jar"/>可以缺省
    /// </summary>
    public string JavaPath { get; init; } = string.Empty;

    /// <summary>
    ///     target启动参数, 若TargetType为Jar, 则为java args。
    /// </summary>
    public string[] Arguments { get; init; } = Array.Empty<string>();

    /// <summary>
    ///     环境变量
    /// </summary>
    public Dictionary<string, PlaceHolderString> Env { get; init; } = new();

    #endregion
}