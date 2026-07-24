using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Plugins;

/// <summary>
/// Validated plugin-private storage under a daemon-owned root with quota.
/// Feature: <c>storage.private</c>. Never returns absolute host paths.
/// </summary>
public interface IPluginPrivateStorage
{
    long QuotaBytes { get; }

    int MaxFiles { get; }

    Task<Result<Unit, DaemonError>> WriteSnapshotAsync<T>(
        string relativePath,
        T value,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : notnull;

    Task<Result<T, DaemonError>> ReadSnapshotAsync<T>(
        string relativePath,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : notnull;

    Task<Result<Unit, DaemonError>> AppendJsonlAsync<T>(
        string relativePath,
        T value,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : notnull;

    Task<Result<Unit, DaemonError>> DeleteAsync(
        string relativePath,
        CancellationToken cancellationToken);
}
