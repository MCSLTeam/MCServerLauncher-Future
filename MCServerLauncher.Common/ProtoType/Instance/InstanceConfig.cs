using System.Text;
using Newtonsoft.Json;
using SysTextJsonRequired = System.Text.Json.Serialization.JsonRequiredAttribute;
using SysTextJsonPropertyName = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace MCServerLauncher.Common.ProtoType.Instance;

/// <summary>
///     实例配置文件, 用于支持Daemon启动MC服务器, 普通Jar文件, 脚本文件, 可执行文件
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
    [SysTextJsonRequired]
    public string Name { get; init; } = null!;

    /// <summary>
    ///     服务器启动目标(jar文件名, 脚本文件名, 可执行文件名)
    /// </summary>
    [JsonProperty(Required = Required.Always)]
    [SysTextJsonRequired]
    public string Target { get; init; } = null!;

    /// <summary>
    ///     默认为MC服务器,实例服务器类型(mcjava, mcfabric, mcforge ...).
    ///     如果不为MC服务器/Terraria/Steam, 则因置为<see cref="InstanceType.Universal" />
    /// </summary>
    [JsonProperty(Required = Required.Always)]
    [SysTextJsonRequired]
    public InstanceType InstanceType { get; init; }

    /// <summary>
    ///     服务器启动目标类型(jar, script[bat, sh], executable)
    /// </summary>
    [JsonProperty(Required = Required.Always)]
    [SysTextJsonRequired]
    public TargetType TargetType { get; init; }

    #endregion

    #region Not Required

    /// <summary>
    ///     服务器Uuid,实例化<see cref="InstanceConfig" />会默认生成
    /// </summary>
    public Guid Uuid = Guid.NewGuid();

    /// <summary>
    ///     实例版本, 可以为空或者null
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    ///     Minecraft版本 (McVersion是Version的逻辑子集，用于Minecraft实例)
    /// </summary>
    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public string McVersion => Version;

    /// <summary>
    ///     Steam服务器版本 (SteamServerVersion是Version的逻辑子集，用于Steam服务器实例)
    /// </summary>
    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public string SteamServerVersion => Version;

    /// <summary>
    ///     Terraria版本 (TerrariaVersion是Version的逻辑子集，用于Terraria实例)
    /// </summary>
    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public string TerrariaVersion => Version;

    /// <summary>
    ///     自定义版本 (CustomVersion是Version的逻辑子集，用于自定义实例)
    /// </summary>
    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public string CustomVersion => Version;

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
    ///     java虚拟机路径, 非MC服务器或<see cref="InstanceConfig.TargetType" />不为<see cref="TargetType.Jar" />可以缺省
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

    /// <summary>
    ///     事件触发器规则
    /// </summary>
    [JsonProperty("event_rules")]
    [SysTextJsonPropertyName("event_rules")]
    public List<EventTrigger.EventRule> EventRules { get; init; } = new();

    #endregion
}
