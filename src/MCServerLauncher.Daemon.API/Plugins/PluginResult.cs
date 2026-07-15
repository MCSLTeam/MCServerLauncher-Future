using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Plugins;

/// <summary>
/// Builds public daemon results without exposing a plugin-specific result type.
/// </summary>
public static class PluginResult
{
    public static Result<Unit, DaemonError> Ok() =>
        Result.Ok<Unit, DaemonError>(Unit.Default);

    public static Result<TResult, DaemonError> Ok<TResult>(TResult value)
        where TResult : notnull =>
        Result.Ok<TResult, DaemonError>(value);

    public static Result<Unit, DaemonError> Fail(PluginError error) =>
        Result.Err<Unit, DaemonError>(error);

    public static Result<TResult, DaemonError> Fail<TResult>(PluginError error)
        where TResult : notnull =>
        Result.Err<TResult, DaemonError>(error);
}

public static class PluginErrorResultExtensions
{
    public static Result<Unit, DaemonError> Fail(
        this IPluginErrorFactory factory,
        string code,
        string message,
        JsonElement? details = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return PluginResult.Fail(factory.Create(code, message, details));
    }

    public static Result<TResult, DaemonError> Fail<TResult>(
        this IPluginErrorFactory factory,
        string code,
        string message,
        JsonElement? details = null)
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(factory);
        return PluginResult.Fail<TResult>(factory.Create(code, message, details));
    }

    public static Result<TResult, DaemonError> AsResult<TResult>(this PluginError error)
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(error);
        return PluginResult.Fail<TResult>(error);
    }
}
