using System.Text.RegularExpressions;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Extensions;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Remote.Event.Extensions;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using MCServerLauncher.Daemon.Utils.LazyCell;
using Microsoft.Extensions.DependencyInjection;
using RustyOptions;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     注册各种Action处理函数
/// </summary>
public static class HandlerRegistration
{
    private static ValueTask<Result<TActionResult, ActionError>> ValueTaskOk<TActionResult>(TActionResult result)
        where TActionResult : IActionResult
    {
        return ValueTask.FromResult(new Result<TActionResult, ActionError>(result));
    }

    private static ValueTask<Result<Unit, ActionError>> ValueTaskOk()
    {
        return ValueTask.FromResult(new Result<Unit, ActionError>(Unit.Default));
    }

    private static ValueTask<Result<Unit, ActionError>> ValueTaskErr(ActionError error)
    {
        return ValueTask.FromResult(new Result<Unit, ActionError>(error));
    }

    private static Result<TActionResult, ActionError> Ok<TActionResult>(TActionResult result)
        where TActionResult : IActionResult
    {
        return new Result<TActionResult, ActionError>(result);
    }

    private static Result<Unit, ActionError> Ok()
    {
        return new Result<Unit, ActionError>(Unit.Default);
    }

    private static Result<TActionResult, ActionError> Err<TActionResult>(ActionError error)
        where TActionResult : IActionResult
    {
        return Result.Err<TActionResult, ActionError>(error);
    }

    private static Result<Unit, ActionError> Err(ActionError error)
    {
        return Result.Err<Unit, ActionError>(error);
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
                (ctx, resolver, ct) => ValueTaskOk(new PingResult
                {
                    Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }))
            .Register(
                ActionType.GetSystemInfo,
                IMatchable.Always(),
                async (ctx, resolver, ct) => Ok(new GetSystemInfoResult
                    { Info = await resolver.GetRequiredService<IAsyncTimedLazyCell<SystemInfo>>().Value })
            )
            .Register(
                ActionType.GetPermissions,
                IMatchable.Always(),
                (ctx, resolver, ct) => ValueTaskOk(new GetPermissionsResult
                {
                    Permissions = ctx.Permissions.PermissionList.Select(p => p.ToString()).ToArray()
                }))
            .Register(
                ActionType.GetJavaList,
                Permission.Of("mcsl.daemon.java_list"),
                async (ctx, resolver, ct) => Ok(new GetJavaListResult
                {
                    JavaList = await resolver.GetRequiredService<IAsyncTimedLazyCell<JavaInfo[]>>().Value
                })
            )

            #endregion

            #region Event

            .Register<SubscribeEventParameter>(
                ActionType.SubscribeEvent,
                IMatchable.Always(),
                (param, ctx, resolver, ct) =>
                    ValueTask.FromResult(ResultExt.Try(wsCtx =>
                        wsCtx.SubscribeEvent(param.Type,
                            param.Type.GetEventMeta(param.Meta, DaemonJsonSettings.Settings)
                        ), ctx).MapErr(ex =>
                        ActionRetcode.ParamError.WithMessage($"Event {param.Type} missing meta").ToError().CauseBy(ex)
                    ))
            )
            .Register<UnsubscribeEventParameter>(
                ActionType.UnsubscribeEvent,
                IMatchable.Always(),
                (param, ctx, resolver, ct) =>
                    ValueTask.FromResult(ResultExt.Try(wsCtx =>
                        wsCtx.UnsubscribeEvent(param.Type,
                            param.Type.GetEventMeta(param.Meta, DaemonJsonSettings.Settings)
                        ), ctx).MapErr(ex =>
                        ActionRetcode.ParamError.WithMessage($"Event {param.Type} missing meta").ToError().CauseBy(ex)
                    ))
            )

            #endregion

            #region File Upload

            .Register<FileUploadRequestParameter, FileUploadRequestResult>(
                ActionType.FileUploadRequest,
                Permission.Of("mcsl.daemon.file.upload"),
                (param, ctx, resolver, ct) =>
                    ValueTask.FromResult(Result
                        .Try(() => FileManager.FileUploadRequest(
                            param.Path,
                            param.Size,
                            param.Timeout.Map(t => TimeSpan.FromMilliseconds(t)),
                            param.Sha1
                        ))
                        .MapErr(ex => new ActionError(ActionRetcode.FileError).CauseBy(ex))
                        .AndThen(fileId => fileId != Guid.Empty
                            ? Ok(new FileUploadRequestResult
                            {
                                FileId = fileId
                            })
                            : Err<FileUploadRequestResult>(
                                ActionRetcode.DiskFull.WithMessage("Failed to pre-allocate space").ToError()
                            ))
                    )
            )
            .Register<FileUploadChunkParameter, FileUploadChunkResult>(
                ActionType.FileUploadChunk,
                Permission.Of("mcsl.daemon.file.upload"),
                async (param, ctx, resolver, ct) =>
                {
                    if (param.FileId == Guid.Empty)
                        return Err<FileUploadChunkResult>(ActionRetcode.NotUploadingDownloading.WithMessage(param.FileId)
                            .ToError());

                    return await ResultExt.TryAsync(async chunkParameter =>
                    {
                        var (done, received) = await FileManager.FileUploadChunk(
                            chunkParameter.FileId,
                            chunkParameter.Offset,
                            chunkParameter.Data
                        );

                        return new FileUploadChunkResult
                        {
                            Done = done,
                            Received = received
                        };
                    }, param).MapTask(result =>
                        result.OrElse(ex => Err<FileUploadChunkResult>(ActionRetcode.FileError.ToError().CauseBy(ex)))
                    );
                })
            .Register<FileUploadCancelParameter>(
                ActionType.FileUploadCancel,
                Permission.Of("mcsl.daemon.file.upload"),
                (param, ctx, resolver, ct) =>
                    FileManager.FileUploadCancel(param.FileId)
                        ? ValueTaskOk()
                        : ValueTaskErr(ActionRetcode.NotUploadingDownloading.WithMessage(param.FileId))
            )

            #endregion

            #region File Download

            .Register<FileDownloadRequestParameter, FileDownloadRequestResult>(
                ActionType.FileDownloadRequest,
                Permission.Of("mcsl.daemon.file.download"),
                async (param, ctx, resolver, ct) =>
                    await ResultExt.TryAsync(async requestParameter =>
                        {
                            return Option.Create(await FileManager.FileDownloadRequest(
                                    requestParameter.Path,
                                    requestParameter.Timeout.Map(t => TimeSpan.FromMilliseconds(t))
                                ))
                                .OkOr(ActionRetcode.RateLimitExceeded.WithMessage(
                                    $"Max download sessions of file '{requestParameter.Path}' reached").ToError())
                                .AndThen(rv =>
                                    Ok(new FileDownloadRequestResult
                                    {
                                        FileId = rv.Id,
                                        Size = rv.Size,
                                        Sha1 = rv.Sha1
                                    }));
                        }, param)
                        .MapTask(result => result.UnwrapOrElse(ex =>
                            Err<FileDownloadRequestResult>(ActionRetcode.FileError.ToError().CauseBy(ex)))
                        )
            )
            .Register<FileDownloadRangeParameter, FileDownloadRangeResult>(
                ActionType.FileDownloadRange,
                Permission.Of("mcsl.daemon.file.download"),
                async (param, ctx, resolver, ct) =>
                {
                    if (!rangePattern.IsMatch(param.Range))
                        return Err<FileDownloadRangeResult>(ActionRetcode.ParamError.WithMessage("Invalid range format")
                            .ToError());

                    var (from, to) = (int.Parse(rangePattern.Match(param.Range).Groups[1].Value),
                        int.Parse(rangePattern.Match(param.Range).Groups[2].Value));

                    return Ok(new FileDownloadRangeResult
                    {
                        Content = await FileManager.FileDownloadRange(param.FileId, from, to)
                    });
                })
            .Register<FileDownloadCloseParameter>(
                ActionType.FileDownloadClose,
                Permission.Of("mcsl.daemon.file.download"),
                (param, ctx, resolver, ct) =>
                {
                    FileManager.FileDownloadClose(param.FileId);
                    return ValueTaskOk();
                })

            #endregion

            #region File Info

            .Register<GetDirectoryInfoParameter, GetDirectoryInfoResult>(
                ActionType.GetDirectoryInfo,
                Permission.Of("mcsl.daemon.file.info.directory"),
                (param, ctx, resolver, ct) =>
                    ValueTask.FromResult(ResultExt.Try(directoryInfoParameter =>
                        {
                            var entry = FileManager.GetDirectoryInfo(directoryInfoParameter.Path);
                            return new GetDirectoryInfoResult
                            {
                                Parent = entry.Parent,
                                Directories = entry.Directories,
                                Files = entry.Files
                            };
                        }, param)
                        .OrElse(ex => ex switch
                        {
                            FileNotFoundException fileNotFoundException => Err<GetDirectoryInfoResult>(
                                ActionRetcode.FileNotFound.ToError().CauseBy(fileNotFoundException)),
                            IOException ioException => Err<GetDirectoryInfoResult>(
                                ActionRetcode.FileError.ToError().CauseBy(ioException)),
                            _ => Err<GetDirectoryInfoResult>(ActionRetcode.FileError.ToError().CauseBy(ex))
                        }))
            )
            .Register<GetFileInfoParameter, GetFileInfoResult>(
                ActionType.GetFileInfo,
                Permission.Of("mcsl.daemon.file.info.file"),
                (param, ctx, resolver, ct) =>
                    ValueTask.FromResult(ResultExt.Try(path =>
                            new GetFileInfoResult
                            {
                                Meta = FileManager.GetFileInfo(path)
                            }, param.Path)
                        .OrElse(ex => ex switch
                        {
                            FileNotFoundException fileNotFoundException => Err<GetFileInfoResult>(
                                ActionRetcode.FileNotFound.ToError().CauseBy(fileNotFoundException)),
                            IOException ioException => Err<GetFileInfoResult>(
                                ActionRetcode.FileError.ToError().CauseBy(ioException)),
                            _ => Err<GetFileInfoResult>(ActionRetcode.FileError.ToError().CauseBy(ex))
                        }))
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

                    if (instance is null)
                        return Err(instanceManager.Instances.ContainsKey(param.Id)
                            ? ActionRetcode.ProcessError.WithMessage("Cannot start instance process")
                            : ActionRetcode.InstanceNotFound.WithMessage(param.Id));


                    instance.OnLog -= eventService.OnInstanceLog;
                    instance.OnLog += eventService.OnInstanceLog;

                    return Ok();
                }
            )
            .Register<StopInstanceParameter>(
                ActionType.StopInstance,
                IMatchable.Always(),
                (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return instanceManager.TryStopInstance(param.Id)
                        ? ValueTaskOk()
                        : ValueTaskErr(instanceManager.Instances.ContainsKey(param.Id)
                            ? ActionRetcode.BadInstanceState.WithMessage($"{param.Id} not running")
                            : ActionRetcode.InstanceNotFound.WithMessage(param.Id));
                })
            .Register<SendToInstanceParameter>(
                ActionType.SendToInstance,
                IMatchable.Always(),
                (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return instanceManager.SendToInstance(param.Id, param.Message)
                        ? ValueTaskOk()
                        : ValueTaskErr(instanceManager.Instances.ContainsKey(param.Id)
                            ? ActionRetcode.BadInstanceState.WithMessage($"{param.Id} not running")
                            : ActionRetcode.InstanceNotFound.WithMessage(param.Id));
                }
            ).Register(
                ActionType.GetAllReports,
                IMatchable.Always(),
                async (ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return Ok(new GetAllReportsResult
                    {
                        Reports = await instanceManager.GetAllReports()
                    });
                })
            .Register<AddInstanceParameter, AddInstanceResult>(
                ActionType.AddInstance,
                IMatchable.Always(), async (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();

                    var validateSettingResult = param.Setting.ValidateSetting().MapErr(innerErr =>
                        new ActionError(ActionRetcode.InstallationError.WithMessage("Invalid instance factory setting"))
                            .WithInner(innerErr));

                    var addInstanceResult =
                        (await validateSettingResult.MapAsTaskAsync(async _ =>
                        {
                            var tryAddInstance = await instanceManager.TryAddInstance(param.Setting);
                            return tryAddInstance.MapErr(err =>
                                new ActionError(ActionRetcode.InstallationError).WithInner(err));
                        })).Flatten();

                    return addInstanceResult.Map(config => new AddInstanceResult
                    {
                        Config = config
                    });
                })
            .Register<RemoveInstanceParameter>(
                ActionType.RemoveInstance,
                IMatchable.Always(),
                (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return instanceManager.TryRemoveInstance(param.Id)
                        ? ValueTaskOk()
                        : ValueTaskErr(instanceManager.RunningInstances.ContainsKey(param.Id)
                            ? ActionRetcode.BadInstanceState.WithMessage($"{param.Id} is running")
                            : ActionRetcode.InstanceNotFound.WithMessage(param.Id));
                })
            .Register<KillInstanceParameter>(
                ActionType.KillInstance,
                IMatchable.Always(),
                (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    instanceManager.KillInstance(param.Id);
                    return ValueTaskOk();
                })
            .Register<GetInstanceReportParameter, GetInstanceReportResult>(
                ActionType.GetInstanceReport,
                IMatchable.Always(),
                async (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return Ok(new GetInstanceReportResult
                    {
                        Report = await instanceManager.GetInstanceReport(param.Id)
                    });
                });

        #endregion
    }
}