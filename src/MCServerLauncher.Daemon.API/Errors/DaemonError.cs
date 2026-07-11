using System.Text.Json;

namespace MCServerLauncher.Daemon.API.Errors;

public enum DaemonErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Permission,
    Storage,
    Transport,
    Internal
}

public abstract class DaemonError
{
    private readonly JsonElement? _details;

    private protected DaemonError(
        string code,
        string message,
        DaemonErrorKind kind,
        JsonElement? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Message = message;
        Kind = kind;
        _details = details?.Clone();
    }

    public string Code { get; }

    public string Message { get; }

    public DaemonErrorKind Kind { get; }

    public JsonElement? Details => _details?.Clone();
}

public sealed class ValidationDaemonError(
    string code,
    string message,
    JsonElement? details = null)
    : DaemonError(code, message, DaemonErrorKind.Validation, details);

public sealed class NotFoundDaemonError(
    string code,
    string message,
    JsonElement? details = null)
    : DaemonError(code, message, DaemonErrorKind.NotFound, details);

public sealed class ConflictDaemonError(
    string code,
    string message,
    JsonElement? details = null)
    : DaemonError(code, message, DaemonErrorKind.Conflict, details);

public sealed class PermissionDaemonError(
    string code,
    string message,
    JsonElement? details = null)
    : DaemonError(code, message, DaemonErrorKind.Permission, details);

public sealed class StorageDaemonError(
    string code,
    string message,
    JsonElement? details = null)
    : DaemonError(code, message, DaemonErrorKind.Storage, details);

public sealed class TransportDaemonError(
    string code,
    string message,
    JsonElement? details = null)
    : DaemonError(code, message, DaemonErrorKind.Transport, details);

public sealed class InternalDaemonError(
    string code,
    string message,
    JsonElement? details = null)
    : DaemonError(code, message, DaemonErrorKind.Internal, details);
