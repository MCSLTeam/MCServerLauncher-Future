using System.Text.RegularExpressions;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Action.Parameters;
using MCServerLauncher.Common.ProtoType.Action.Results;
using MCServerLauncher.Common.System;
using MCServerLauncher.Daemon.Minecraft.Server;
using MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils.Cache;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action;

public class ActionHandlerRegistry
{
    private readonly JsonSerializer _jsonSerializer;

    public ActionHandlerRegistry(IWebJsonConverter webJsonConverter)
    {
        _jsonSerializer = webJsonConverter.GetSerializer();
    }

    public Dictionary<ActionType, Func<JToken?, IResolver, CancellationToken, ValueTask<IActionResult?>>>
        Handlers { get; } = new();

    // private readonly Dictionary<ActionType, Func<JToken, IResolver, IActionResult>> _syncHandlers = new();

    public Dictionary<ActionType, IMatchable> HandlerPermissions { get; } = new();

    # region Register async handlers

    public ActionHandlerRegistry Register<TParam, TResult>(
        ActionType actionType,
        IMatchable actionPermission,
        Func<TParam, IResolver, CancellationToken, ValueTask<TResult>> handler
    )
        where TParam : class, IActionParameter
        where TResult : class, IActionResult
    {
        SetActionPermission(actionType, actionPermission);
        Handlers[actionType] = async (paramToken, resolver, cancellationToken) =>
        {
            var param = ParseParameter<TParam>(paramToken, _jsonSerializer);
            return await handler(param, resolver, cancellationToken);
        };
        return this;
    }

    public ActionHandlerRegistry Register<TParam>(
        ActionType actionType,
        IMatchable actionPermission,
        Func<TParam, IResolver, CancellationToken, ValueTask> handler
    )
        where TParam : class, IActionParameter
    {
        SetActionPermission(actionType, actionPermission);
        Handlers[actionType] = async (paramToken, resolver, cancellationToken) =>
        {
            var param = ParseParameter<TParam>(paramToken, _jsonSerializer);
            await handler(param, resolver, cancellationToken);
            return null;
        };
        return this;
    }

    public ActionHandlerRegistry Register<TResult>(
        ActionType actionType,
        IMatchable actionPermission,
        Func<IResolver, CancellationToken, ValueTask<TResult>> handler
    )
        where TResult : class, IActionResult
    {
        SetActionPermission(actionType, actionPermission);
        Handlers[actionType] = async (_, resolver, cancellationToken) =>
        {
            var result = await handler(resolver, cancellationToken);
            return result;
        };
        return this;
    }

    public ActionHandlerRegistry Register(
        ActionType actionType,
        IMatchable actionPermission,
        Func<IResolver, CancellationToken, ValueTask> handler
    )
    {
        SetActionPermission(actionType, actionPermission);
        Handlers[actionType] = async (_, resolver, cancellationToken) =>
        {
            await handler(resolver, cancellationToken);
            return null;
        };
        return this;
    }

    private void SetActionPermission(ActionType actionType, IMatchable actionPermission)
    {
        HandlerPermissions[actionType] = actionPermission;
    }

    # endregion

    private static TParam ParseParameter<TParam>(JToken? paramToken, JsonSerializer serializer)
        where TParam : class, IActionParameter
    {
        if (paramToken is null)
            throw new ActionExecutionException(1501, "Parameter is null");

        try
        {
            return paramToken.ToObject<TParam>(serializer)!;
        }
        catch (JsonException e)
        {
            throw new ActionExecutionException(1502, "Could not deserialize param: " + e.Message, e);
        }
        catch (NullReferenceException e)
        {
            throw new ActionExecutionException(1502, "Could not deserialize param", e);
        }
        catch (Exception e)
        {
            throw new ActionExecutionException(1500, "Error occurred during param deserialization: " + e.Message, e);
        }
    }
}

public static class HandlerRegistration
{
    public static ActionHandlerRegistry RegisterHandlers(this ActionHandlerRegistry registry)
    {
        Regex rangePattern = new(@"^(\d+)..(\d+)$");
        return registry

            #region MISC

            .Register(
                ActionType.Ping,
                IMatchable.Always(),
                (resolver, ct) =>
                    ValueTask.FromResult(new PingResult(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())))
            .Register(
                ActionType.GetSystemInfo,
                IMatchable.Always(),
                async (resolver, ct) => new GetSystemInfoResult(await SystemInfo.Get()))
            .Register(
                ActionType.GetPermissions,
                IMatchable.Always(),
                (resolver, ct) =>
                {
                    var ctx = resolver.GetRequiredService<WsServiceContext>();
                    return ValueTask.FromResult(
                        new GetPermissionsResult(
                            ctx.Permissions.PermissionList.Select(p => p.ToString()).ToArray()
                        )
                    );
                })
            .Register(
                ActionType.GetJavaList,
                Permission.Of("mcsl.daemon.java_list"),
                async (resolver, ct) =>
                {
                    var ctx = resolver.GetRequiredService<IAsyncTimedCacheable<List<JavaInfo>>>();
                    return new GetJavaListResult(await ctx.Value);
                }
            )

            #endregion

            #region Event

            .Register<SubscribeEventParameter>(
                ActionType.SubscribeEvent,
                IMatchable.Always(),
                (param, resolver, ct) =>
                {
                    var ctx = resolver.GetRequiredService<WsServiceContext>();
                    var serializer = resolver.GetRequiredService<IWebJsonConverter>().GetSerializer();
                    try
                    {
                        ctx.SubscribeEvent(param.Type, param.Type.GetEventMeta(param.Meta, serializer));
                    }
                    catch (NullReferenceException e)
                    {
                        throw new ActionExecutionException(1500,
                            $"Event(Type={param.Type}) missing required event meta", e);
                    }

                    return ValueTask.CompletedTask;
                }
            )
            .Register<UnsubscribeEventParameter>(
                ActionType.UnsubscribeEvent,
                IMatchable.Always(), (param, resolver, ct) =>
                {
                    var ctx = resolver.GetRequiredService<WsServiceContext>();
                    try
                    {
                        ctx.UnsubscribeEvent(param.Type,
                            param.Type.GetEventMeta(param.Meta,
                                resolver.GetRequiredService<IWebJsonConverter>().GetSerializer()));
                    }
                    catch (NullReferenceException e)
                    {
                        throw new ActionExecutionException(1500,
                            $"Event(Type={param.Type}) missing required event meta", e);
                    }

                    return ValueTask.CompletedTask;
                })

            #endregion

            #region File Upload

            .Register<FileUploadRequestParameter, FileUploadRequestResult>(
                ActionType.FileUploadRequest,
                Permission.Of("mcsl.daemon.file.upload"),
                (param, resolver, ct) =>
                {
                    var fileId = FileManager.FileUploadRequest(
                        param.Path,
                        param.Size,
                        param.Timeout.Map(t => TimeSpan.FromMilliseconds(t)),
                        param.Sha1
                    );
                    if (fileId == Guid.Empty)
                        throw new ActionExecutionException(1401, "Failed to pre-allocate space");

                    return ValueTask.FromResult(new FileUploadRequestResult(fileId));
                }
            )
            .Register<FileUploadChunkParameter, FileUploadChunkResult>(
                ActionType.FileUploadChunk,
                Permission.Of("mcsl.daemon.file.upload"),
                async (param, resolver, ct) =>
                {
                    if (param.FileId == Guid.Empty)
                        throw new ActionExecutionException(1402, "Invalid file id");

                    var (done, received) = await FileManager.FileUploadChunk(param.FileId,
                        param.Offset, param.Data);

                    return new FileUploadChunkResult(done, received);
                })
            .Register<FileUploadCancelParameter>(
                ActionType.FileUploadCancel,
                Permission.Of("mcsl.daemon.file.upload"),
                (param, resolver, ct) =>
                {
                    if (!FileManager.FileUploadCancel(param.FileId))
                        throw new ActionExecutionException(1402, "Failed to cancel file upload");
                    return ValueTask.CompletedTask;
                })

            #endregion

            #region File Download

            .Register<FileDownloadRequestParameter, FileDownloadRequestResult>(
                ActionType.FileDownloadRequest,
                Permission.Of("mcsl.daemon.file.download"),
                async (param, resolver, ct) =>
                {
                    var (fileId, size, sha1) = await FileManager.FileDownloadRequest(
                        param.Path,
                        param.Timeout.Map(t => TimeSpan.FromMilliseconds(t))
                    );
                    return new FileDownloadRequestResult(fileId, size, sha1);
                }
            )
            .Register<FileDownloadRangeParameter, FileDownloadRangeResult>(
                ActionType.FileDownloadRange,
                Permission.Of("mcsl.daemon.file.download"),
                async (param, resolver, ct) =>
                {
                    if (!rangePattern.IsMatch(param.Range))
                        throw new ActionExecutionException(1403, "Invalid range format");

                    var (from, to) = (int.Parse(rangePattern.Match(param.Range).Groups[1].Value),
                        int.Parse(rangePattern.Match(param.Range).Groups[2].Value));
                    return new FileDownloadRangeResult(
                        await FileManager.FileDownloadRange(param.FileId, from, to));
                })
            .Register<FileDownloadCloseParameter>(
                ActionType.FileDownloadClose,
                Permission.Of("mcsl.daemon.file.download"),
                (param, resolver, ct) =>
                {
                    FileManager.FileDownloadClose(param.FileId);
                    return ValueTask.CompletedTask;
                })

            #endregion

            #region File Info

            .Register<GetDirectoryInfoParameter, GetDirectoryInfoResult>(
                ActionType.GetDirectoryInfo,
                Permission.Of("mcsl.daemon.file.info.directory"),
                (param, resolver, ct) =>
                {
                    var entry = FileManager.GetDirectoryInfo(param.Path);
                    return ValueTask.FromResult(
                        new GetDirectoryInfoResult(entry.Parent, entry.Files, entry.Directories));
                }
            )
            .Register<GetFileInfoParameter, GetFileInfoResult>(
                ActionType.GetFileInfo,
                Permission.Of("mcsl.daemon.file.info.file"),
                (param, resolver, ct) =>
                    ValueTask.FromResult(
                        new GetFileInfoResult(
                            FileManager.GetFileInfo(param.Path)))
            )

            #endregion

            #region Instance

            .Register<StartInstanceParameter, StartInstanceResult>(
                ActionType.StartInstance,
                IMatchable.Always(),
                (param, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    var eventService = resolver.GetRequiredService<IEventService>();

                    var rv = instanceManager.TryStartInstance(param.Id, out var instance);
                    if (instance == null) throw new ActionExecutionException(1400, "Instance not found");

                    if (rv)
                    {
                        Action<string?> handler = msg =>
                        {
                            if (msg != null)
                                eventService.OnInstanceLog(param.Id, msg);
                        };
                        instance.OnLog -= handler;
                        instance.OnLog += handler;
                    }

                    return ValueTask.FromResult(new StartInstanceResult(rv));
                }
            )
            .Register<StopInstanceParameter, StopInstanceResult>(
                ActionType.StopInstance,
                IMatchable.Always(),
                (param, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return ValueTask.FromResult(
                        new StopInstanceResult(
                            instanceManager.TryStopInstance(param.Id)));
                })
            .Register<SendToInstanceParameter>(
                ActionType.SendToInstance,
                IMatchable.Always(),
                (param, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    instanceManager.SendToInstance(param.Id, param.Message);
                    return ValueTask.CompletedTask;
                }
            ).Register(
                ActionType.GetAllStatus,
                IMatchable.Always(),
                (resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return ValueTask.FromResult(
                        new GetAllStatusResult(
                            instanceManager.GetAllStatus()));
                });

        #endregion
    }
}