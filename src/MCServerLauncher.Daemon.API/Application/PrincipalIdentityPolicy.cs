using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.API.Application;

internal static class PrincipalIdentityPolicy
{
    internal const string GlobalOwnerPrincipal = "*";
    internal const string MainTokenSubject = "daemon-main";
    internal const string PluginHostSubjectPrefix = "plugin:";

    internal static bool IsValidExternalSubject(string? subject) =>
        !string.IsNullOrWhiteSpace(subject) &&
        !IsReservedExternalSubject(subject);

    internal static bool IsMainTokenSubject(string? subject) =>
        string.Equals(subject, MainTokenSubject, StringComparison.Ordinal);

    internal static bool IsValidOwnershipSubject(string? subject)
    {
        if (IsValidExternalSubject(subject))
            return true;
        if (subject is null || !subject.StartsWith(PluginHostSubjectPrefix, StringComparison.Ordinal))
            return false;

        var pluginId = subject[PluginHostSubjectPrefix.Length..];
        try
        {
            _ = ProtocolIdentifier.ValidatePluginId(pluginId, nameof(subject));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static void ValidateExternalSubject(string subject, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject, parameterName);
        if (IsReservedExternalSubject(subject))
        {
            throw new ArgumentException(
                "The subject is reserved for daemon-owned identities.",
                parameterName);
        }
    }

    internal static void ValidateMainTokenSubject(string subject, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject, parameterName);
        if (!IsMainTokenSubject(subject))
        {
            throw new ArgumentException(
                $"The main-token subject must be '{MainTokenSubject}'.",
                parameterName);
        }
    }

    private static bool IsReservedExternalSubject(string subject) =>
        string.Equals(subject, GlobalOwnerPrincipal, StringComparison.Ordinal) ||
        IsMainTokenSubject(subject) ||
        subject.StartsWith(PluginHostSubjectPrefix, StringComparison.Ordinal);
}
