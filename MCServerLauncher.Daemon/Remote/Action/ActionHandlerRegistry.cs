using System.Text.RegularExpressions;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.Utils;
using MCServerLauncher.Daemon.Minecraft.Server;
using MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using MCServerLauncher.Daemon.Utils.Cache;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action;

public class ActionHandlerRegistry
{
    public Dictionary<
            ActionType,
            Func<JToken?, IResolver, CancellationToken, ValueTask<IActionResult?>>
        >
        Handlers { get; } = new();

    // private readonly Dictionary<ActionType, Func<JToken, IResolver, IActionResult>> _syncHandlers = new();

    public Dictionary<ActionType, IMatchable> HandlerPermissions { get; } = new();

    private static TParam ParseParameter<TParam>(JToken? paramToken)
        where TParam : class, IActionParameter
    {
        ActionExceptionHelper.ThrowIf(paramToken is null, ActionReturnCode.ParameterIsNull, "Parameter is null");

        try
        {
            // paramToken一定不为null
            return paramToken!.ToObject<TParam>(JsonSerializer.Create(DaemonJsonSettings.Settings))!;
        }
        catch (JsonSerializationException e)
        {
            var @params = e.Path?.Split(new[] { '.' }).ToArray();
            ActionExceptionHelper.ThrowIf(@params is null, ActionReturnCode.ParameterParseError,
                "Could not deserialize param");

            throw e.Context(
                ActionReturnCode.ParameterParseError,
                $"Could not deserialize param '{@params![1]}', in json path: '{e.Path}'"
            );
        }
        catch (NullReferenceException e)
        {
            throw e.Context(ActionReturnCode.ParameterParseError, "Could not deserialize param");
        }
        catch (Exception e)
        {
            throw e.Context("Error occurred during param deserialization: " + e.Message);
        }
    }

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
            var param = ParseParameter<TParam>(paramToken);
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
            var param = ParseParameter<TParam>(paramToken);
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
}

public static class HandlerRegistration
{
    private static void OnInstanceLogHandler(IEventService eventService, Guid guid, string? message)
    {
        if (message is not null) eventService.OnInstanceLog(guid, message);
    }

    // TODO 权限完善
    public static ActionHandlerRegistry RegisterHandlers(this ActionHandlerRegistry registry)
    {
        Regex rangePattern = new(@"^(\d+)..(\d+)$");
        return registry

            #region MISC

            .Register(
                ActionType.Ping,
                IMatchable.Always(),
                (resolver, ct) =>
                    ValueTask.FromResult(new PingResult
                    {
                        Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }))
            .Register(
                ActionType.GetSystemInfo,
                IMatchable.Always(),
                async (resolver, ct) =>
                {
                    return new GetSystemInfoResult
                    {
                        Info = await SystemInfo.Get()
                    };
                })
            .Register(
                ActionType.GetPermissions,
                IMatchable.Always(),
                (resolver, ct) =>
                {
                    var ctx = resolver.GetRequiredService<WsServiceContext>();
                    return ValueTask.FromResult(new GetPermissionsResult
                    {
                        Permissions = ctx.Permissions.PermissionList.Select(p => p.ToString()).ToArray()
                    });
                })
            .Register(
                ActionType.GetJavaList,
                Permission.Of("mcsl.daemon.java_list"),
                async (resolver, ct) =>
                {
                    var ctx = resolver.GetRequiredService<IAsyncTimedCacheable<List<JavaInfo>>>();
                    return new GetJavaListResult
                    {
                        JavaList = await ctx.Value
                    };
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
                    try
                    {
                        ctx.SubscribeEvent(param.Type,
                            param.Type.GetEventMeta(param.Meta, DaemonJsonSettings.Settings));
                    }
                    catch (NullReferenceException e)
                    {
                        throw e.Context(ActionReturnCode.ParameterIsNull,
                            $"Event(Type={param.Type}) missing required event meta"
                        );
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
                            param.Type.GetEventMeta(param.Meta, DaemonJsonSettings.Settings));
                    }
                    catch (NullReferenceException e)
                    {
                        throw e.Context(ActionReturnCode.ParameterIsNull,
                            $"Event(Type={param.Type}) missing required event meta"
                        );
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
                    Guid fileId;
                    try
                    {
                        fileId = FileManager.FileUploadRequest(
                            param.Path,
                            param.Size,
                            param.Timeout.Map(t => TimeSpan.FromMilliseconds(t)),
                            param.Sha1
                        );
                    }
                    catch (IOException e)
                    {
                        throw e.Context(ActionReturnCode.IOException);
                    }

                    ActionExceptionHelper.ThrowIf(
                        fileId == Guid.Empty,
                        ActionReturnCode.IOException,
                        "Failed to pre-allocate space"
                    );

                    return ValueTask.FromResult(new FileUploadRequestResult
                    {
                        FileId = fileId
                    });
                }
            )
            .Register<FileUploadChunkParameter, FileUploadChunkResult>(
                ActionType.FileUploadChunk,
                Permission.Of("mcsl.daemon.file.upload"),
                async (param, resolver, ct) =>
                {
                    ActionExceptionHelper.ThrowIf(
                        param.FileId == Guid.Empty,
                        ActionReturnCode.FileNotFound,
                        "Invalid file id"
                    );

                    try
                    {
                        var (done, received) = await FileManager.FileUploadChunk(param.FileId,
                            param.Offset, param.Data);

                        return new FileUploadChunkResult
                        {
                            Done = done,
                            Received = received
                        };
                    }
                    catch (IOException e)
                    {
                        throw e.Context(ActionReturnCode.IOException);
                    }
                })
            .Register<FileUploadCancelParameter>(
                ActionType.FileUploadCancel,
                Permission.Of("mcsl.daemon.file.upload"),
                (param, resolver, ct) =>
                {
                    ActionExceptionHelper.ThrowIf(
                        !FileManager.FileUploadCancel(param.FileId),
                        ActionReturnCode.FileNotFound,
                        "Failed to cancel file upload"
                    );
                    return ValueTask.CompletedTask;
                })

            #endregion

            #region File Download

            .Register<FileDownloadRequestParameter, FileDownloadRequestResult>(
                ActionType.FileDownloadRequest,
                Permission.Of("mcsl.daemon.file.download"),
                async (param, resolver, ct) =>
                {
                    try
                    {
                        var rv = await FileManager.FileDownloadRequest(
                            param.Path,
                            param.Timeout.Map(t => TimeSpan.FromMilliseconds(t))
                        );
                        ActionExceptionHelper.ThrowIf(
                            rv is null,
                            ActionReturnCode.RequestLimitReached,
                            $"Max download sessions of file '{param.Path}' reached"
                        );
                        var (fileId, size, sha1) = rv!.Value;
                        return new FileDownloadRequestResult
                        {
                            FileId = fileId,
                            Size = size,
                            Sha1 = sha1
                        };
                    }
                    catch (IOException e)
                    {
                        throw e.Context(ActionReturnCode.IOException);
                    }
                }
            )
            .Register<FileDownloadRangeParameter, FileDownloadRangeResult>(
                ActionType.FileDownloadRange,
                Permission.Of("mcsl.daemon.file.download"),
                async (param, resolver, ct) =>
                {
                    ActionExceptionHelper.ThrowIf(
                        !rangePattern.IsMatch(param.Range),
                        ActionReturnCode.ParameterError,
                        "Invalid range format"
                    );

                    var (from, to) = (int.Parse(rangePattern.Match(param.Range).Groups[1].Value),
                        int.Parse(rangePattern.Match(param.Range).Groups[2].Value));
                    return new FileDownloadRangeResult
                    {
                        Content = await FileManager.FileDownloadRange(param.FileId, from, to)
                    };
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
                    try
                    {
                        var entry = FileManager.GetDirectoryInfo(param.Path);
                        return ValueTask.FromResult(
                            new GetDirectoryInfoResult
                            {
                                Parent = entry.Parent,
                                Directories = entry.Directories,
                                Files = entry.Files
                            });
                    }
                    catch (FileNotFoundException e)
                    {
                        throw e.Context(ActionReturnCode.FileNotFound);
                    }
                    catch (IOException e)
                    {
                        throw e.Context(ActionReturnCode.IOException);
                    }
                }
            )
            .Register<GetFileInfoParameter, GetFileInfoResult>(
                ActionType.GetFileInfo,
                Permission.Of("mcsl.daemon.file.info.file"),
                (param, resolver, ct) =>
                {
                    try
                    {
                        return ValueTask.FromResult(
                            new GetFileInfoResult
                            {
                                Meta = FileManager.GetFileInfo(param.Path)
                            });
                    }
                    catch (FileNotFoundException e)
                    {
                        throw e.Context(ActionReturnCode.FileNotFound);
                    }
                    catch (IOException e)
                    {
                        throw e.Context(ActionReturnCode.IOException);
                    }
                }
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

                    ActionExceptionHelper.ThrowIf(
                        instance is null,
                        ActionReturnCode.InstanceNotFound,
                        "Instance not found"
                    );

                    if (rv)
                    {
                        instance!.OnLog -= eventService.OnInstanceLog;
                        instance!.OnLog += eventService.OnInstanceLog;
                    }

                    return ValueTask.FromResult(new StartInstanceResult
                    {
                        Done = rv
                    });
                }
            )
            .Register<StopInstanceParameter, StopInstanceResult>(
                ActionType.StopInstance,
                IMatchable.Always(),
                (param, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return ValueTask.FromResult(
                        new StopInstanceResult
                        {
                            Done = instanceManager.TryStopInstance(param.Id)
                        });
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
                async (resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return new GetAllStatusResult
                    {
                        Status = await instanceManager.GetAllStatus()
                    };
                })
            .Register<AddInstanceParameter, AddInstanceResult>(
                ActionType.AddInstance,
                IMatchable.Always(), async (param, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return new AddInstanceResult
                    {
                        Done = await instanceManager.TryAddInstance(param.Setting)
                    };
                })
            .Register<RemoveInstanceParameter, RemoveInstanceResult>(
                ActionType.RemoveInstance,
                IMatchable.Always(),
                async (param, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return new RemoveInstanceResult
                    {
                        Done = await instanceManager.TryRemoveInstance(param.Id)
                    };
                })
            .Register<KillInstanceParameter>(
                ActionType.KillInstance,
                IMatchable.Always(),
                async (param, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    await instanceManager.KillInstance(param.Id);
                })
            .Register<GetInstanceStatusParameter, GetInstanceStatusResult>(
                ActionType.GetInstanceStatus,
                IMatchable.Always(),
                async (param, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return new GetInstanceStatusResult
                    {
                        Status = await instanceManager.GetInstanceStatus(param.Id)
                    };
                });

        #endregion
    }
}