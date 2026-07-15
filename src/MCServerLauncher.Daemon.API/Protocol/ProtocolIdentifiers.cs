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

public sealed record PluginCapability
{
    public static readonly PluginCapability RpcRegister = new("rpc.register");
    public static readonly PluginCapability EventPublish = new("event.publish");
    public static readonly PluginCapability InstanceQuery = new("instance.query");

    public PluginCapability(string value) => Value = ProtocolIdentifier.ValidatePluginCapability(value, nameof(value));

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

    public static string ValidatePluginCapability(string value, string parameterName) =>
        ValidateDottedIdentifier(value, parameterName, "Plugin capability");

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
