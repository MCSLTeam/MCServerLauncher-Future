using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.System;
using ContractDriveInfo = MCServerLauncher.Common.Contracts.System.DriveInfo;

namespace MCServerLauncher.Common.Contracts.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    Converters =
    [
        typeof(InstanceTypeJsonConverter),
        typeof(TargetTypeJsonConverter),
        typeof(SourceTypeJsonConverter),
        typeof(InstanceFactoryMirrorJsonConverter),
        typeof(InstanceStatusJsonConverter)
    ])]
[JsonSerializable(typeof(EventRuleQuery))]
[JsonSerializable(typeof(EventRuleSet))]
[JsonSerializable(typeof(EventRuleUpdateRequest))]
[JsonSerializable(typeof(PathRequest))]
[JsonSerializable(typeof(PathRenameRequest))]
[JsonSerializable(typeof(PathTransferRequest))]
[JsonSerializable(typeof(DeleteDirectoryRequest))]
[JsonSerializable(typeof(FileSystemMetadata))]
[JsonSerializable(typeof(FileMetadata))]
[JsonSerializable(typeof(DirectoryMetadata))]
[JsonSerializable(typeof(FileEntry))]
[JsonSerializable(typeof(DirectoryEntry))]
[JsonSerializable(typeof(FileDetails))]
[JsonSerializable(typeof(DirectoryDetails))]
[JsonSerializable(typeof(UploadOpenRequest))]
[JsonSerializable(typeof(UploadSession))]
[JsonSerializable(typeof(UploadChunkRequest))]
[JsonSerializable(typeof(DownloadOpenRequest))]
[JsonSerializable(typeof(DownloadSession))]
[JsonSerializable(typeof(DownloadChunkRequest))]
[JsonSerializable(typeof(DownloadChunk))]
[JsonSerializable(typeof(InstanceConfiguration))]
[JsonSerializable(typeof(InstanceFactoryConfiguration))]
[JsonSerializable(typeof(CreateInstanceRequest))]
[JsonSerializable(typeof(CreateInstanceResult))]
[JsonSerializable(typeof(InstanceReference))]
[JsonSerializable(typeof(InstanceCommandRequest))]
[JsonSerializable(typeof(InstanceCoreReplacementRequest))]
[JsonSerializable(typeof(UpdateInstanceSettingsRequest))]
[JsonSerializable(typeof(InstanceInstallMetadata))]
[JsonSerializable(typeof(InstanceSettingsResult))]
[JsonSerializable(typeof(UpdateInstanceSettingsResult))]
[JsonSerializable(typeof(InstancePlayer))]
[JsonSerializable(typeof(InstancePerformance))]
[JsonSerializable(typeof(InstanceReport))]
[JsonSerializable(typeof(InstanceReportList))]
[JsonSerializable(typeof(InstanceLogQuery))]
[JsonSerializable(typeof(InstanceLogResult))]
[JsonSerializable(typeof(OperatingSystemInfo))]
[JsonSerializable(typeof(ProcessorInfo))]
[JsonSerializable(typeof(MemoryInfo))]
[JsonSerializable(typeof(ContractDriveInfo))]
[JsonSerializable(typeof(SystemInfo))]
[JsonSerializable(typeof(JavaRuntime))]
[JsonSerializable(typeof(JavaRuntimeList))]
public partial class ApplicationContractJsonContext : JsonSerializerContext;
