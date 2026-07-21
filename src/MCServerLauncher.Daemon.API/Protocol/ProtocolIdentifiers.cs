namespace MCServerLauncher.Daemon.API.Protocol;

public sealed record RpcMethod
{
    public RpcMethod(string value) => Value = ProtocolIdentifier.ValidateRpcMethod(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record EventName
{
    public EventName(string value) => Value = ProtocolIdentifier.ValidateEventName(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record PermissionName
{
    public PermissionName(string value) => Value = ProtocolIdentifier.ValidatePermissionName(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record PluginFeature
{
    public static readonly PluginFeature RpcRegister = new("rpc.register");
    public static readonly PluginFeature EventPublish = new("event.publish");
    public static readonly PluginFeature EventSubscribe = new("event.subscribe");
    public static readonly PluginFeature InstanceQuery = new("instance.query");
    public static readonly PluginFeature InstanceManage = new("instance.manage");
    public static readonly PluginFeature FileRead = new("file.read");
    public static readonly PluginFeature FileWrite = new("file.write");
    public static readonly PluginFeature SystemQuery = new("system.query");
    public static readonly PluginFeature EventRuleManage = new("event-rule.manage");
    public static readonly PluginFeature OperationQuery = new("operation.query");
    public static readonly PluginFeature OperationCancel = new("operation.cancel");
    public static readonly PluginFeature ProvisioningManage = new("provisioning.manage");
    public static readonly PluginFeature BackupManage = new("backup.manage");
    public static readonly PluginFeature MonitoringQuery = new("monitoring.query");
    public static readonly PluginFeature AutomationManage = new("automation.manage");
    public static readonly PluginFeature AuditQuery = new("audit.query");
    public static readonly PluginFeature StoragePrivate = new("storage.private");
    public static readonly PluginFeature NetworkHttpListen = new("network.http.listen");
    public static readonly PluginFeature AuthVerify = new("auth.verify");

    public PluginFeature(string value) => Value = ProtocolIdentifier.ValidatePluginFeature(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;
}

internal static class ProtocolIdentifier
{
    public static string ValidatePluginId(string value, string parameterName) =>
        ValidateDottedIdentifier(value, parameterName, "Plugin id", allowHyphen: true, allowUnderscore: false);

    public static string ValidateRpcMethod(string value, string parameterName) =>
        ValidateDottedIdentifier(value, parameterName, "RPC method");

    public static string ValidateEventName(string value, string parameterName) =>
        ValidateDottedIdentifier(value, parameterName, "Event name");

    public static string ValidatePermissionName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        return value == "*"
            ? value
            : ValidateDottedIdentifier(value, parameterName, "Permission name");
    }

    public static string ValidatePluginFeature(string value, string parameterName) =>
        ValidateDottedIdentifier(value, parameterName, "Plugin feature");

    private static string ValidateDottedIdentifier(
        string value,
        string parameterName,
        string kind,
        bool allowHyphen = true,
        bool allowUnderscore = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var segmentStart = true;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var isAsciiLetter = character is >= 'a' and <= 'z';
            var isDigit = character is >= '0' and <= '9';

            if (character == '.')
            {
                if (segmentStart ||
                    index == value.Length - 1 ||
                    value[index - 1] is '-' or '_')
                {
                    throw new ArgumentException($"{kind} must contain dot-separated segments that start and end with a lowercase ASCII letter or digit.", parameterName);
                }

                segmentStart = true;
                continue;
            }

            if (segmentStart && !isAsciiLetter && !isDigit)
            {
                throw new ArgumentException($"{kind} segments must start with a lowercase ASCII letter or digit.", parameterName);
            }

            if (!isAsciiLetter &&
                !isDigit &&
                (!allowUnderscore || character != '_') &&
                (!allowHyphen || character != '-'))
            {
                throw new ArgumentException(
                    allowHyphen && allowUnderscore
                        ? $"{kind} must use lowercase ASCII letters, digits, dots, hyphens, or underscores."
                        : allowHyphen
                            ? $"{kind} must use lowercase ASCII letters, digits, dots, or hyphens."
                            : $"{kind} must use lowercase ASCII letters, digits, dots, or underscores.",
                    parameterName);
            }

            if (index == value.Length - 1 && (character is '-' or '_'))
            {
                throw new ArgumentException($"{kind} segments must end with a lowercase ASCII letter or digit.", parameterName);
            }

            segmentStart = false;
        }

        return value;
    }
}
