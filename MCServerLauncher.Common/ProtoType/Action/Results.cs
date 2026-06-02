using System;
using MCServerLauncher.Common.ProtoType.Files;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.ProtoType.Status;
using SysTextJsonRequired = System.Text.Json.Serialization.JsonRequiredAttribute;
using SysTextJsonPropertyName = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace MCServerLauncher.Common.ProtoType.Action;

public interface IActionResult
{
}

public sealed record EmptyActionResult : IActionResult;

public sealed record GetPermissionsResult : IActionResult
{
    [SysTextJsonRequired]
    public string[] Permissions { get; init; } = null!;
}

public sealed record PingResult : IActionResult
{
    [SysTextJsonRequired]
    public long Time { get; init; }
}

public sealed record GetJavaListResult : IActionResult
{
    [SysTextJsonRequired]
    public JavaInfo[] JavaList { get; init; } = null!;
}

public sealed record FileUploadRequestResult : IActionResult
{
    [SysTextJsonRequired]
    public Guid FileId { get; init; }
}

public sealed record FileUploadChunkResult : IActionResult
{
    [SysTextJsonRequired]
    public bool Done { get; init; }

    [SysTextJsonRequired]
    public long Received { get; init; }
}

public sealed record FileDownloadRequestResult : IActionResult
{
    [SysTextJsonRequired]
    public Guid FileId { get; init; }

    [SysTextJsonRequired]
    public long Size { get; init; }

    [SysTextJsonRequired]
    public string Sha1 { get; init; } = null!;
}

public sealed record FileDownloadRangeResult : IActionResult
{
    [SysTextJsonRequired]
    public string Content { get; init; } = null!;
}

public sealed record GetFileInfoResult : IActionResult
{
    [SysTextJsonRequired]
    public FileMetadata Meta { get; init; } = null!;
}

public sealed record GetDirectoryInfoResult : IActionResult
{
    public string? Parent { get; init; }

    [SysTextJsonRequired]
    public DirectoryEntry.FileInformation[] Files { get; init; } = null!;

    [SysTextJsonRequired]
    public DirectoryEntry.DirectoryInformation[] Directories { get; init; } = null!;
}

public sealed record AddInstanceResult : IActionResult
{
    [SysTextJsonRequired]
    public InstanceConfig Config { get; init; } = null!;
}

public sealed record GetInstanceReportResult : IActionResult
{
    [SysTextJsonRequired]
    public InstanceReport Report { get; init; } = null!;
}

public sealed record GetInstanceLogHistoryResult : IActionResult
{
    [SysTextJsonRequired]
    public string[] Logs { get; init; } = null!;
}

public sealed record InstanceInstallMetadata
{
    [SysTextJsonRequired]
    public string InstallerKind { get; init; } = null!;

    public string? InstallerSourcePath { get; init; }

    [SysTextJsonRequired]
    public string[] GeneratedPaths { get; init; } = [];

    public string? ResolvedLaunchTarget { get; init; }

    public DateTimeOffset InstalledAt { get; init; }
}

public sealed record GetInstanceSettingsResult : IActionResult
{
    [SysTextJsonRequired]
    public InstanceConfig Config { get; init; } = null!;

    [SysTextJsonRequired]
    public string WorkingDirectory { get; init; } = null!;

    [SysTextJsonRequired]
    public bool CurrentTargetExists { get; init; }

    [SysTextJsonRequired]
    public bool CanEdit { get; init; }

    public string? EditBlockedReason { get; init; }

    public InstanceInstallMetadata? InstallMetadata { get; init; }
}

public sealed record UpdateInstanceSettingsResult : IActionResult
{
    [SysTextJsonRequired]
    public InstanceConfig Config { get; init; } = null!;

    [SysTextJsonRequired]
    public bool RequiresRestart { get; init; }

    [SysTextJsonRequired]
    public bool Reinstalled { get; init; }

    [SysTextJsonRequired]
    public string[] DeletedGeneratedPaths { get; init; } = [];

    [SysTextJsonRequired]
    public string[] PreservedOriginalPaths { get; init; } = [];
}

public sealed record GetAllReportsResult : IActionResult
{
    [SysTextJsonRequired]
    public Dictionary<Guid, InstanceReport> Reports { get; init; } = null!;
}

public sealed record GetSystemInfoResult : IActionResult
{
    [SysTextJsonRequired]
    public SystemInfo Info { get; init; } = null!;
}

public sealed record GetEventRulesResult : IActionResult
{
    [SysTextJsonPropertyName("rules")]
    public List<EventTrigger.EventRule> Rules { get; init; } = new();
}
