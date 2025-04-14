using System.Text.RegularExpressions;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Daemon.Minecraft.Server;
using MCServerLauncher.Daemon.Minecraft.Server.Factory;
using MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using MCServerLauncher.Daemon.Utils.Cache;
using Microsoft.Extensions.DependencyInjection;
using SystemInfoHelper = MCServerLauncher.Daemon.Utils.Status.SystemInfoHelper;

namespace MCServerLauncher.Daemon.Remote.Action;

public static class HandlerRegistration
{
    // TODO 权限完善
    public static ActionHandlerRegistry RegisterHandlers(this ActionHandlerRegistry registry)
    {
        Regex rangePattern = new(@"^(\d+)..(\d+)$");
        return registry

            #region MISC

            .Register(
                ActionType.Ping,
                IMatchable.Always(),
                (ctx, resolver, ct) =>
                    ValueTask.FromResult(new PingResult
                    {
                        Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }))
            .Register(
                ActionType.GetSystemInfo,
                IMatchable.Always(),
                async (ctx, resolver, ct) => new GetSystemInfoResult { Info = await SystemInfoHelper.GetSystemInfo() }
            )
            .Register(
                ActionType.GetPermissions,
                IMatchable.Always(),
                (ctx, resolver, ct) =>
                {
                    return ValueTask.FromResult(new GetPermissionsResult
                    {
                        Permissions = ctx.Permissions.PermissionList.Select(p => p.ToString()).ToArray()
                    });
                })
            .Register(
                ActionType.GetJavaList,
                Permission.Of("mcsl.daemon.java_list"),
                async (ctx, resolver, ct) =>
                {
                    var cache = resolver.GetRequiredService<IAsyncTimedCacheable<List<JavaInfo>>>();
                    return new GetJavaListResult
                    {
                        JavaList = await cache.Value
                    };
                }
            )

            #endregion

            #region Event

            .Register<SubscribeEventParameter>(
                ActionType.SubscribeEvent,
                IMatchable.Always(),
                (param, ctx, resolver, ct) =>
                {
                    try
                    {
                        ctx.SubscribeEvent(param.Type,
                            param.Type.GetEventMeta(param.Meta, DaemonJsonSettings.Settings));
                    }
                    catch (NullReferenceException e)
                    {
                        throw e.Context(
                            ActionRetcode.ParamError.WithMessage(
                                $"Event {param.Type} missing meta")
                        );
                    }

                    return ValueTask.CompletedTask;
                }
            )
            .Register<UnsubscribeEventParameter>(
                ActionType.UnsubscribeEvent,
                IMatchable.Always(), (param, ctx, resolver, ct) =>
                {
                    try
                    {
                        ctx.UnsubscribeEvent(param.Type,
                            param.Type.GetEventMeta(param.Meta, DaemonJsonSettings.Settings));
                    }
                    catch (NullReferenceException e)
                    {
                        throw e.Context(
                            ActionRetcode.ParamError.WithMessage(
                                $"Event {param.Type} missing meta")
                        );
                    }

                    return ValueTask.CompletedTask;
                })

            #endregion

            #region File Upload

            .Register<FileUploadRequestParameter, FileUploadRequestResult>(
                ActionType.FileUploadRequest,
                Permission.Of("mcsl.daemon.file.upload"),
                (param, ctx, resolver, ct) =>
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
                        throw e.Context(ActionRetcode.FileError);
                    }

                    ActionExceptionHelper.ThrowIf(
                        fileId == Guid.Empty,
                        ActionRetcode.DiskFull.WithMessage("Failed to pre-allocate space")
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
                async (param, ctx, resolver, ct) =>
                {
                    ActionExceptionHelper.ThrowIf(
                        param.FileId == Guid.Empty,
                        ActionRetcode.NotUploading.WithMessage(param.FileId)
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
                        throw e.Context(ActionRetcode.FileError);
                    }
                })
            .Register<FileUploadCancelParameter>(
                ActionType.FileUploadCancel,
                Permission.Of("mcsl.daemon.file.upload"),
                (param, ctx, resolver, ct) =>
                {
                    ActionExceptionHelper.ThrowIf(
                        !FileManager.FileUploadCancel(param.FileId),
                        ActionRetcode.NotUploading.WithMessage(param.FileId)
                    );
                    return ValueTask.CompletedTask;
                })

            #endregion

            #region File Download

            .Register<FileDownloadRequestParameter, FileDownloadRequestResult>(
                ActionType.FileDownloadRequest,
                Permission.Of("mcsl.daemon.file.download"),
                async (param, ctx, resolver, ct) =>
                {
                    try
                    {
                        var rv = await FileManager.FileDownloadRequest(
                            param.Path,
                            param.Timeout.Map(t => TimeSpan.FromMilliseconds(t))
                        );
                        ActionExceptionHelper.ThrowIf(
                            rv is null,
                            ActionRetcode.RateLimitExceeded.WithMessage(
                                $"Max download sessions of file '{param.Path}' reached")
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
                        throw e.Context(ActionRetcode.FileError);
                    }
                }
            )
            .Register<FileDownloadRangeParameter, FileDownloadRangeResult>(
                ActionType.FileDownloadRange,
                Permission.Of("mcsl.daemon.file.download"),
                async (param, ctx, resolver, ct) =>
                {
                    ActionExceptionHelper.ThrowIf(
                        !rangePattern.IsMatch(param.Range),
                        ActionRetcode.ParamError.WithMessage("Invalid range format")
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
                (param, ctx, resolver, ct) =>
                {
                    FileManager.FileDownloadClose(param.FileId);
                    return ValueTask.CompletedTask;
                })

            #endregion

            #region File Info

            .Register<GetDirectoryInfoParameter, GetDirectoryInfoResult>(
                ActionType.GetDirectoryInfo,
                Permission.Of("mcsl.daemon.file.info.directory"),
                (param, ctx, resolver, ct) =>
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
                        throw e.Context(ActionRetcode.FileNotFound);
                    }
                    catch (IOException e)
                    {
                        throw e.Context(ActionRetcode.FileError);
                    }
                }
            )
            .Register<GetFileInfoParameter, GetFileInfoResult>(
                ActionType.GetFileInfo,
                Permission.Of("mcsl.daemon.file.info.file"),
                (param, ctx, resolver, ct) =>
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
                        throw e.Context(ActionRetcode.FileNotFound);
                    }
                    catch (IOException e)
                    {
                        throw e.Context(ActionRetcode.FileError);
                    }
                }
            )

            #endregion

            #region Instance

            .Register<StartInstanceParameter>(
                ActionType.StartInstance,
                IMatchable.Always(),
                async (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    var eventService = resolver.GetRequiredService<IEventService>();

                    var instance = await instanceManager.TryStartInstance(param.Id);

                    ActionExceptionHelper.ThrowIf(
                        instance is null && !instanceManager.Instances.ContainsKey(param.Id),
                        ActionRetcode.InstanceNotFound.WithMessage(param.Id)
                    );

                    ActionExceptionHelper.ThrowIf(
                        instance is null && instanceManager.Instances.ContainsKey(param.Id),
                        ActionRetcode.ProcessError.WithMessage("Cannot start instance process")
                    );


                    instance!.OnLog -= eventService.OnInstanceLog;
                    instance!.OnLog += eventService.OnInstanceLog;
                }
            )
            .Register<StopInstanceParameter>(
                ActionType.StopInstance,
                IMatchable.Always(),
                (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return instanceManager.TryStopInstance(param.Id)
                        ? ValueTask.CompletedTask
                        : ValueTask.FromException(
                            instanceManager.Instances.ContainsKey(param.Id)
                                ? new ActionException(
                                    ActionRetcode.BadInstanceState.WithMessage($"{param.Id} not running")
                                )
                                : new ActionException(
                                    ActionRetcode.InstanceNotFound.WithMessage(param.Id)
                                )
                        );
                })
            .Register<SendToInstanceParameter>(
                ActionType.SendToInstance,
                IMatchable.Always(),
                (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    if (!instanceManager.SendToInstance(param.Id, param.Message))
                        return ValueTask.FromException(
                            instanceManager.Instances.ContainsKey(param.Id)
                                ? new ActionException(
                                    ActionRetcode.BadInstanceState.WithMessage($"{param.Id} not running")
                                )
                                : new ActionException(
                                    ActionRetcode.InstanceNotFound.WithMessage(param.Id)
                                )
                        );

                    return ValueTask.CompletedTask;
                }
            ).Register(
                ActionType.GetAllStatus,
                IMatchable.Always(),
                async (ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return new GetAllStatusResult
                    {
                        Status = await instanceManager.GetAllStatus()
                    };
                })
            .Register<AddInstanceParameter, AddInstanceResult>(
                ActionType.AddInstance,
                IMatchable.Always(), async (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();

                    if (!param.Setting.ValidateSetting())
                        throw new ActionException(
                            ActionRetcode.InstallationError.WithMessage(
                                "Invalid instance factory setting") // TODO 更多报错信息
                        );

                    var config = await instanceManager.TryAddInstance(param.Setting);
                    if (config is null)
                        throw new ActionException(
                            ActionRetcode.InstallationError.WithMessage("Failed to add instance") // TODO 更多报错信息
                        );

                    return new AddInstanceResult
                    {
                        Config = config
                    };
                })
            .Register<RemoveInstanceParameter>(
                ActionType.RemoveInstance,
                IMatchable.Always(),
                (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return instanceManager.TryRemoveInstance(param.Id)
                        ? ValueTask.CompletedTask
                        : ValueTask.FromException(instanceManager.RunningInstances.ContainsKey(param.Id)
                            ? new ActionException(
                                ActionRetcode.BadInstanceState.WithMessage($"{param.Id} is running")
                            )
                            : new ActionException(
                                ActionRetcode.InstanceNotFound.WithMessage(param.Id)
                            ));
                })
            .Register<KillInstanceParameter>(
                ActionType.KillInstance,
                IMatchable.Always(),
                async (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    await instanceManager.KillInstance(param.Id);
                })
            .Register<GetInstanceStatusParameter, GetInstanceStatusResult>(
                ActionType.GetInstanceStatus,
                IMatchable.Always(),
                async (param, ctx, resolver, ct) =>
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