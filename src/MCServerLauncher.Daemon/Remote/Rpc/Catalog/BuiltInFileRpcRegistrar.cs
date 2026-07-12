using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

internal static class BuiltInFileRpcRegistrar
{
    public static void Register(ProtocolCatalogBuilder builder, IFileApplication application)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(application);

        Register<PathRequest, UnitResult>(builder, "mcsl.directory.create", async (request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await application.CreateDirectoryAsync(request, token).ConfigureAwait(false)));
        Register<DeleteDirectoryRequest, UnitResult>(builder, "mcsl.directory.delete", async (request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await application.DeleteDirectoryAsync(request, token).ConfigureAwait(false)));
        Register<PathRequest, DirectoryDetails>(builder, "mcsl.directory.info.get", async (request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.GetDirectoryInfoAsync(request, token).ConfigureAwait(false)));
        Register<PathTransferRequest, UnitResult>(builder, "mcsl.directory.move", async (request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await application.MoveDirectoryAsync(request, token).ConfigureAwait(false)));
        Register<PathRenameRequest, UnitResult>(builder, "mcsl.directory.rename", async (request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await application.RenameDirectoryAsync(request, token).ConfigureAwait(false)));
        Register<PathTransferRequest, UnitResult>(builder, "mcsl.directory.copy", async (request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await application.CopyDirectoryAsync(request, token).ConfigureAwait(false)));
        Register<PathTransferRequest, UnitResult>(builder, "mcsl.file.copy", async (request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await application.CopyFileAsync(request, token).ConfigureAwait(false)));
        Register<PathRequest, UnitResult>(builder, "mcsl.file.delete", async (request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await application.DeleteFileAsync(request, token).ConfigureAwait(false)));
        RegisterSession<FileSessionReference, UnitResult>(builder, "mcsl.file.download.close", async (operations, request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await operations.CloseDownloadAsync(request.SessionId, token).ConfigureAwait(false)));
        RegisterSession<DownloadOpenRequest, DownloadSession>(builder, "mcsl.file.download.open", async (operations, request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await operations.OpenDownloadAsync(request, token).ConfigureAwait(false)));
        RegisterSession<DownloadChunkRequest, DownloadReadResult>(builder, "mcsl.file.download.read", async (operations, request, token) =>
        {
            var result = await operations.ReadDownloadChunkAsync(request, token).ConfigureAwait(false);
            if (result.IsErr(out _))
            {
                return ProtocolRpcExecution<DownloadReadResult>.Err(result.UnwrapErr());
            }

            var chunk = result.Unwrap();
            var metadata = new DownloadReadResult(request.SessionId, chunk.Offset, chunk.Data.Length, chunk.IsFinal);
            return ProtocolRpcExecution<DownloadReadResult>.DownloadOk(
                metadata,
                new ProtocolDownloadAttachment(request.SessionId, chunk.Offset, chunk.Data, chunk.IsFinal));
        });
        Register<PathRequest, FileDetails>(builder, "mcsl.file.info.get", async (request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.GetFileInfoAsync(request, token).ConfigureAwait(false)));
        Register<PathTransferRequest, UnitResult>(builder, "mcsl.file.move", async (request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await application.MoveFileAsync(request, token).ConfigureAwait(false)));
        Register<PathRenameRequest, UnitResult>(builder, "mcsl.file.rename", async (request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await application.RenameFileAsync(request, token).ConfigureAwait(false)));
        RegisterSession<FileSessionReference, UnitResult>(builder, "mcsl.file.upload.cancel", async (operations, request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await operations.CancelUploadAsync(request.SessionId, token).ConfigureAwait(false)));
        RegisterSession<FileSessionReference, UnitResult>(builder, "mcsl.file.upload.close", async (operations, request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await operations.CloseUploadAsync(request.SessionId, token).ConfigureAwait(false)));
        RegisterSession<UploadOpenRequest, UploadSession>(builder, "mcsl.file.upload.open", async (operations, request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await operations.OpenUploadAsync(request, token).ConfigureAwait(false)));
    }

    private static void Register<TRequest, TResult>(
        ProtocolCatalogBuilder builder,
        string method,
        Func<TRequest, CancellationToken, Task<ProtocolRpcExecution<TResult>>> handler)
        where TResult : notnull
    {
        var descriptor = (RpcDescriptor<TRequest, TResult>)BuiltInProtocolDefinitions.Rpcs.Single(
            candidate => StringComparer.Ordinal.Equals(candidate.Method.Value, method));
        builder.RegisterBuiltInRpc(
            descriptor,
            new RpcBinding<TRequest, TResult>(
                ProtocolExecutionOwner.BuiltIn,
                (_, request, token) => handler(request, token)));
    }

    private static void RegisterSession<TRequest, TResult>(
        ProtocolCatalogBuilder builder,
        string method,
        Func<IProtocolFileSessionOperations, TRequest, CancellationToken, Task<ProtocolRpcExecution<TResult>>> handler)
        where TResult : notnull
    {
        var descriptor = (RpcDescriptor<TRequest, TResult>)BuiltInProtocolDefinitions.Rpcs.Single(
            candidate => StringComparer.Ordinal.Equals(candidate.Method.Value, method));
        builder.RegisterBuiltInRpc(
            descriptor,
            new RpcBinding<TRequest, TResult>(
                ProtocolExecutionOwner.BuiltIn,
                (context, request, token) => context.FileSessionOperations is { } operations
                    ? handler(operations, request, token)
                    : Task.FromResult(ProtocolRpcExecution<TResult>.Err(
                        new InternalDaemonError(
                            "file.session.context_missing",
                            "The connection file-session context is unavailable.")))));
    }
}
