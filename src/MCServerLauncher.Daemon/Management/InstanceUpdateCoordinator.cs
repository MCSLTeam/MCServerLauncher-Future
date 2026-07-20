using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using Serilog;

namespace MCServerLauncher.Daemon.Management;

internal sealed class InstanceUpdateCoordinator
{
    private static readonly StringComparer FactoryWorkspaceNameComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly string[] FactoryWorkspaceExcludedPaths =
    [
        InstanceConfig.FileName,
        InstanceConfig.FileName + ".bak",
        InstanceInstallMetadataStore.FileName,
        InstanceInstallMetadataStore.FileName + ".bak"
    ];

    private readonly Func<InstanceConfig, IInstance> _instanceFactory;
    private readonly InstanceManager _instanceManager;

    public InstanceUpdateCoordinator(
        InstanceManager instanceManager,
        Func<InstanceConfig, IInstance> instanceFactory)
    {
        _instanceManager = instanceManager;
        _instanceFactory = instanceFactory;
    }

    public Result<InstanceSettingsResult, DaemonError> GetInstanceSettings(
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_instanceManager.Instances.TryGetValue(instanceId, out var instance))
        {
            return ResultExt.Err<InstanceSettingsResult>(
                new NotFoundDaemonError("instance.not_found", $"Instance '{instanceId}' was not found."));
        }

        var config = CloneConfig(instance.Config);
        var workingDirectory = config.GetWorkingDirectory();
        var currentTargetExists = InstanceTargetPathValidator.TryResolveTargetFile(
            workingDirectory,
            config.Target,
            out var currentTargetPath,
            out _) && File.Exists(currentTargetPath);
        InstanceInstallMetadata? metadata;
        try
        {
            metadata = InstanceInstallMetadataStore.Read(workingDirectory);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[InstanceUpdateCoordinator] Failed to read instance installation metadata.");
            return ResultExt.Err<InstanceSettingsResult>(new StorageDaemonError(
                "instance.install_metadata.read_failed",
                "The instance installation metadata could not be read."));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var canEdit = instance.Status is InstanceStatus.Stopped or InstanceStatus.Crashed;

        return ResultExt.Ok(new InstanceSettingsResult(
            InstanceConfigurationMapper.ToContract(config),
            workingDirectory,
            currentTargetExists,
            canEdit,
            canEdit ? null : $"Instance is {instance.Status}",
            metadata));
    }

    public async Task<Result<UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettings(
        UpdateInstanceSettingsRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var mutation = await _instanceManager.AcquireInstanceMutationAsync(request.InstanceId, ct);
        if (!_instanceManager.Instances.TryGetValue(request.InstanceId, out var instance))
        {
            return ResultExt.Err<UpdateInstanceSettingsResult>(
                new NotFoundDaemonError("instance.not_found", $"Instance '{request.InstanceId}' was not found."));
        }

        if (instance.Status is not InstanceStatus.Stopped and not InstanceStatus.Crashed)
        {
            return ResultExt.Err<UpdateInstanceSettingsResult>(
                new ConflictDaemonError(
                    "instance.running",
                    $"Instance '{request.InstanceId}' must be stopped before updating settings."));
        }

        var currentConfig = instance.Config;
        var workingDirectory = currentConfig.GetWorkingDirectory();
        var preservedPaths = new List<string>();
        var deletedGeneratedPaths = new List<string>();
        var reinstalled = false;
        var requiresRestart = false;

        var target = currentConfig.Target;
        var version = request.Version ?? currentConfig.Version;
        var instanceType = request.InstanceType;
        string? replacementSourcePath = null;
        string? stagedReplacementCore = null;
        string? originalTargetPath = null;
        string? replacementTargetPath = null;
        StorageMutationJournal? storageJournal = null;
        (Guid InstanceId, string WorkingDirectory)? factoryWorkspace = null;
        InstanceInstallMetadata? replacementInstallMetadata = null;
        FilePairMutationJournal? installMetadataJournal = null;
        var generatedPaths = Array.Empty<(string RelativePath, string FullPath, bool Existed)>();

        if (request.ReplacementCore != null)
        {
            if (!InstanceTargetPathValidator.TryResolveTargetFile(
                    workingDirectory,
                    currentConfig.Target,
                    out var resolvedOriginalTargetPath,
                    out var currentTargetError))
            {
                return ResultExt.Err<UpdateInstanceSettingsResult>(currentTargetError!);
            }

            try
            {
                replacementSourcePath = FileManager.ResolveAndValidatePath(request.ReplacementCore.UploadedSourcePath);
            }
            catch (Exception exception)
            {
                Log.Warning(
                    exception,
                    "[InstanceUpdateCoordinator] Rejected replacement core source path {Source}.",
                    request.ReplacementCore.UploadedSourcePath);
                return ResultExt.Err<UpdateInstanceSettingsResult>(new ValidationDaemonError(
                    "instance.replacement_core.invalid",
                    "The replacement core source path is invalid."));
            }

            if (!File.Exists(replacementSourcePath))
            {
                return ResultExt.Err<UpdateInstanceSettingsResult>(
                    new NotFoundDaemonError(
                        "instance.replacement_core.not_found",
                        $"Replacement core not found at {request.ReplacementCore.UploadedSourcePath}."));
            }

            target = request.ReplacementCore.PreferredTargetName ?? Path.GetFileName(replacementSourcePath);
            if (!InstanceTargetPathValidator.TryResolveTargetFile(
                    workingDirectory,
                    target,
                    out replacementTargetPath,
                    out var replacementTargetError))
            {
                return ResultExt.Err<UpdateInstanceSettingsResult>(replacementTargetError!);
            }

            originalTargetPath = resolvedOriginalTargetPath;
            if (HasWindowsCaseAlias(workingDirectory, originalTargetPath) ||
                HasWindowsCaseAlias(workingDirectory, replacementTargetPath))
            {
                return ResultExt.Err<UpdateInstanceSettingsResult>(new ValidationDaemonError(
                    "instance.target.case_conflict",
                    "Instance target casing conflicts with an existing Windows path."));
            }

            requiresRestart = true;
        }

        var newConfig = currentConfig with
        {
            Name = request.Name,
            InstanceType = instanceType,
            JavaPath = request.JavaPath ?? currentConfig.JavaPath,
            Arguments = request.Arguments.ToArray(),
            Version = version,
            Target = target,
            ConsoleMode = request.ConsoleMode ?? currentConfig.ConsoleMode
        };

        var validation = newConfig.ValidateConfig();
        if (validation.IsErr(out var validationError))
        {
            return ResultExt.Err<UpdateInstanceSettingsResult>(validationError!);
        }

        InstanceInstallMetadata? installMetadata;
        try
        {
            ct.ThrowIfCancellationRequested();
            installMetadata = InstanceInstallMetadataStore.Read(workingDirectory);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[InstanceUpdateCoordinator] Failed to read instance installation metadata.");
            return ResultExt.Err<UpdateInstanceSettingsResult>(new StorageDaemonError(
                "instance.install_metadata.read_failed",
                "The instance installation metadata could not be read."));
        }
        var shouldRerunInstaller = request.ForceRerunInstaller || RequiresInstaller(instanceType);

        if (request.ReplacementCore is not null &&
            !shouldRerunInstaller &&
            (Directory.Exists(replacementTargetPath!) ||
             (!PathsEqual(replacementTargetPath!, originalTargetPath!) && File.Exists(replacementTargetPath))))
        {
            return ResultExt.Err<UpdateInstanceSettingsResult>(new ConflictDaemonError(
                "instance.replacement_core.target_conflict",
                "The replacement target conflicts with an existing instance path."));
        }

        if (request.ReplacementCore is not null && shouldRerunInstaller)
        {
            var validatedGeneratedPaths = new List<(string RelativePath, string FullPath, bool Existed)>();
            var metadataGeneratedPaths = installMetadata is null || installMetadata.GeneratedPaths.IsDefault
                ? ImmutableArray<string>.Empty
                : installMetadata.GeneratedPaths;
            foreach (var generatedPath in metadataGeneratedPaths.Distinct(StringComparer.Ordinal))
            {
                if (!InstanceTargetPathValidator.TryResolveGeneratedPath(
                        workingDirectory,
                        generatedPath,
                        out var fullGeneratedPath,
                        out var generatedPathError))
                {
                    return ResultExt.Err<UpdateInstanceSettingsResult>(generatedPathError!);
                }

                if (HasWindowsCaseAlias(workingDirectory, fullGeneratedPath))
                {
                    return ResultExt.Err<UpdateInstanceSettingsResult>(new ValidationDaemonError(
                        "instance.generated_path.invalid",
                        "Instance installation metadata contains case-conflicting Windows paths."));
                }

                if (validatedGeneratedPaths.Any(path => PathsEqual(path.FullPath, fullGeneratedPath)))
                {
                    continue;
                }

                validatedGeneratedPaths.Add((
                    generatedPath,
                    fullGeneratedPath,
                    File.Exists(fullGeneratedPath) || Directory.Exists(fullGeneratedPath)));
            }

            generatedPaths = [.. validatedGeneratedPaths];
        }

        if (request.ReplacementCore is not null)
        {
            try
            {
                storageJournal = new StorageMutationJournal(workingDirectory);
                if (shouldRerunInstaller)
                {
                    factoryWorkspace = storageJournal.CreateFactoryWorkspace();
                }
                else
                {
                    storageJournal.Stage(originalTargetPath!);
                    storageJournal.Stage(replacementTargetPath!);
                    stagedReplacementCore = storageJournal.CreateStagedFilePath();
                    File.Copy(replacementSourcePath!, stagedReplacementCore, true);
                }
            }
            catch (Exception exception)
            {
                TryRollback(storageJournal, "staging the instance core replacement");
                Log.Error(exception, "[InstanceUpdateCoordinator] Failed to stage instance core replacement");
                return ResultExt.Err<UpdateInstanceSettingsResult>(
                    new StorageDaemonError(
                        "instance.replacement.stage_failed",
                        "Failed to stage instance core replacement."));
            }

            if (ct.IsCancellationRequested)
            {
                TryRollback(storageJournal, "canceling the staged instance core replacement");
                ct.ThrowIfCancellationRequested();
            }
        }

        if (request.ReplacementCore != null && shouldRerunInstaller)
        {
            Result<InstanceConfiguration, DaemonError> factoryResult;
            try
            {
                var setting = new InstanceFactoryConfiguration(
                    InstanceConfigurationMapper.WithInstanceId(
                        InstanceConfigurationMapper.ToContract(newConfig),
                        factoryWorkspace!.Value.InstanceId),
                    replacementSourcePath!,
                    SourceType.Core,
                    InstanceFactoryMirror.None,
                    false);
                factoryResult = await setting.ApplyInstanceFactory(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                TryRollback(storageJournal, "canceling the replacement instance factory");
                throw;
            }
            catch (Exception exception)
            {
                TryRollback(storageJournal, "handling a replacement instance factory failure");
                Log.Error(exception, "[InstanceUpdateCoordinator] Failed to run replacement instance factory.");
                return ResultExt.Err<UpdateInstanceSettingsResult>(new InternalDaemonError(
                    "instance.factory.failed",
                    "The replacement instance factory failed."));
            }
            if (ct.IsCancellationRequested)
            {
                TryRollback(storageJournal, "canceling the completed replacement instance factory");
                ct.ThrowIfCancellationRequested();
            }
            if (factoryResult.IsErr(out var installError))
            {
                TryRollback(storageJournal, "handling a replacement instance factory error result");
                return ResultExt.Err<UpdateInstanceSettingsResult>(installError!);
            }

            try
            {
                var factoryConfig = InstanceConfigurationMapper.WithInstanceId(
                    factoryResult.Unwrap(),
                    request.InstanceId);
                newConfig = InstanceConfigurationMapper.ToInstanceConfig(factoryConfig) with
                {
                    Name = newConfig.Name,
                    Uuid = newConfig.Uuid,
                    Env = newConfig.Env,
                    EventRules = newConfig.EventRules,
                    InputEncoding = newConfig.InputEncoding,
                    OutputEncoding = newConfig.OutputEncoding
                };

                var factoryConfigValidation = newConfig.ValidateConfig();
                if (factoryConfigValidation.IsErr(out var factoryConfigError) ||
                    !InstanceTargetPathValidator.TryResolveTargetFile(
                        factoryWorkspace.Value.WorkingDirectory,
                        newConfig.Target,
                        out var factoryTargetPath,
                        out _) ||
                    !File.Exists(factoryTargetPath))
                {
                    TryRollback(storageJournal, "rejecting an invalid replacement instance factory result");
                    Log.Error(
                        "[InstanceUpdateCoordinator] Replacement factory returned an invalid configuration for instance {InstanceId}: {Error}",
                        request.InstanceId,
                        factoryConfigError?.Message ?? "the launch target was not produced");
                    return ResultExt.Err<UpdateInstanceSettingsResult>(new InternalDaemonError(
                        "instance.factory.invalid_result",
                        "The replacement instance factory returned an invalid configuration."));
                }

                replacementInstallMetadata = new InstanceInstallMetadata(
                    newConfig.InstanceType.ToString(),
                    replacementSourcePath,
                    DetectFactoryGeneratedPaths(factoryWorkspace.Value.WorkingDirectory),
                    newConfig.Target,
                    DateTimeOffset.UtcNow);
            }
            catch (Exception exception)
            {
                TryRollback(storageJournal, "handling invalid replacement instance factory output");
                Log.Error(exception, "[InstanceUpdateCoordinator] Failed to inspect replacement instance factory output.");
                return ResultExt.Err<UpdateInstanceSettingsResult>(new StorageDaemonError(
                    "instance.factory.output_inspection_failed",
                    "The replacement instance factory output could not be inspected."));
            }

            reinstalled = true;
        }

        if (request.ReplacementCore is not null && !shouldRerunInstaller)
        {
            replacementInstallMetadata = new InstanceInstallMetadata(
                newConfig.InstanceType.ToString(),
                replacementSourcePath,
                installMetadata is null || installMetadata.GeneratedPaths.IsDefault
                    ? ImmutableArray<string>.Empty
                    : installMetadata.GeneratedPaths,
                newConfig.Target,
                DateTimeOffset.UtcNow);
        }

        if (request.ReplacementCore is not null)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var installMetadataPath = InstanceInstallMetadataStore.GetPath(workingDirectory);
                installMetadataJournal = FilePairMutationJournal.Capture(installMetadataPath);

                if (factoryWorkspace is not null)
                {
                    storageJournal!.Stage(originalTargetPath!);
                    foreach (var (generatedPath, fullGeneratedPath, existed) in generatedPaths)
                    {
                        storageJournal.Stage(fullGeneratedPath);
                        if (existed && !PathsEqual(fullGeneratedPath, originalTargetPath!))
                            deletedGeneratedPaths.Add(generatedPath);
                    }

                    storageJournal.ApplyDirectoryTree(
                        factoryWorkspace.Value.WorkingDirectory,
                        ct,
                        FactoryWorkspaceExcludedPaths);
                }
                else
                {
                    File.Move(stagedReplacementCore!, replacementTargetPath!, true);
                    if (!File.Exists(replacementTargetPath))
                    {
                        throw new IOException(
                            $"Replacement target '{replacementTargetPath}' was not materialized as a regular file.");
                    }
                }

                InstanceInstallMetadataStore.Write(workingDirectory, replacementInstallMetadata!);
                ct.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                TryRollback(storageJournal, "canceling the instance storage commit");
                TryRestoreFilePair(installMetadataJournal, "instance installation metadata");
                throw;
            }
            catch (Exception exception)
            {
                TryRollback(storageJournal, "handling an instance storage commit failure");
                TryRestoreFilePair(installMetadataJournal, "instance installation metadata");
                Log.Error(exception, "[InstanceUpdateCoordinator] Failed to commit replacement instance storage");
                return ResultExt.Err<UpdateInstanceSettingsResult>(
                    new StorageDaemonError(
                        "instance.settings.commit_failed",
                        "Failed to commit instance settings."));
            }
        }

        IInstance replacement;
        try
        {
            replacement = _instanceFactory(newConfig);
        }
        catch (Exception exception)
        {
            TryRollback(storageJournal, "handling updated instance construction failure");
            TryRestoreFilePair(installMetadataJournal, "instance installation metadata");
            Log.Error(exception, "[InstanceUpdateCoordinator] Failed to construct updated instance");
            return ResultExt.Err<UpdateInstanceSettingsResult>(
                new InternalDaemonError(
                    "instance.construct_failed",
                    "Failed to construct updated instance."));
        }

        var configPath = Path.Combine(workingDirectory, InstanceConfig.FileName);
        FilePairMutationJournal? configJournal = null;
        try
        {
            configJournal = FilePairMutationJournal.Capture(configPath);
            FileManager.WriteJsonAndBackup(configPath, newConfig);
        }
        catch (Exception exception)
        {
            TryDisposeReplacement(replacement);
            TryRollback(storageJournal, "handling instance settings persistence failure");
            TryRestoreFilePair(installMetadataJournal, "instance installation metadata");
            TryRestoreFilePair(configJournal, "instance settings");
            Log.Error(exception, "[InstanceUpdateCoordinator] Failed to persist instance settings");
            return ResultExt.Err<UpdateInstanceSettingsResult>(
                new StorageDaemonError(
                    "instance.settings.persist_failed",
                    "Failed to persist instance settings."));
        }

        try
        {
            storageJournal?.PrepareCommit(originalTargetPath, preservedPaths);
        }
        catch (Exception exception)
        {
            TryDisposeReplacement(replacement);
            TryRollback(storageJournal, "handling instance settings preparation failure");
            TryRestoreFilePair(installMetadataJournal, "instance installation metadata");
            TryRestoreFilePair(configJournal, "instance settings");
            Log.Error(exception, "[InstanceUpdateCoordinator] Failed to commit instance settings");
            return ResultExt.Err<UpdateInstanceSettingsResult>(
                new StorageDaemonError(
                    "instance.settings.commit_failed",
                    "Failed to commit instance settings."));
        }

        try
        {
            _instanceManager.ReplaceInstanceWithinAdmission(request.InstanceId, replacement);
        }
        catch (Exception exception)
        {
            TryDisposeReplacement(replacement);
            TryRollback(storageJournal, "handling updated instance publication failure");
            TryRestoreFilePair(installMetadataJournal, "instance installation metadata");
            TryRestoreFilePair(configJournal, "instance settings");
            Log.Error(exception, "[InstanceUpdateCoordinator] Failed to commit updated instance");
            return ResultExt.Err<UpdateInstanceSettingsResult>(
                new InternalDaemonError(
                    "instance.update.commit_failed",
                    "Failed to commit updated instance."));
        }

        if (storageJournal is not null)
        {
            try
            {
                storageJournal.CompleteCommit();
            }
            catch (Exception exception)
            {
                Log.Warning(
                    exception,
                    "[InstanceUpdateCoordinator] Updated instance '{InstanceId}' but could not remove transaction staging '{StagingDirectory}'",
                    request.InstanceId,
                    storageJournal.StagingDirectory);
            }
        }

        return ResultExt.Ok(new UpdateInstanceSettingsResult(
            InstanceConfigurationMapper.ToContract(newConfig),
            requiresRestart,
            reinstalled,
            deletedGeneratedPaths.ToImmutableArray(),
            preservedPaths.ToImmutableArray()));
    }

    private static bool RequiresInstaller(InstanceType instanceType)
    {
        return instanceType is InstanceType.MCForge or InstanceType.MCNeoForge or InstanceType.MCCleanroom;
    }

    private static ImmutableArray<string> DetectFactoryGeneratedPaths(string workingDirectory)
    {
        var exclusions = FactoryWorkspaceExcludedPaths
            .Select(NormalizeRelativePath)
            .ToHashSet(FactoryWorkspaceNameComparer);

        return Directory
            .EnumerateDirectories(workingDirectory, "*", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(workingDirectory, "*", SearchOption.TopDirectoryOnly))
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(workingDirectory, path)))
            .Where(path => !exclusions.Contains(path))
            .OrderBy(static path => path, FactoryWorkspaceNameComparer)
            .ThenBy(static path => path, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static bool PathsEqual(string left, string right)
    {
        return StringComparer.Ordinal.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right));
    }

    private static bool HasWindowsCaseAlias(string rootDirectory, string path)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var fullRoot = Path.GetFullPath(rootDirectory);
        var relativePath = Path.GetRelativePath(fullRoot, Path.GetFullPath(path));
        var currentDirectory = fullRoot;
        foreach (var component in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Directory.Exists(currentDirectory))
                return false;

            foreach (var entry in Directory.EnumerateFileSystemEntries(currentDirectory))
            {
                var existingName = Path.GetFileName(entry);
                if (string.Equals(existingName, component, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(existingName, component, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            currentDirectory = Path.Combine(currentDirectory, component);
        }

        return false;
    }

    private static InstanceConfig CloneConfig(InstanceConfig config)
    {
        return config with
        {
            Arguments = [.. config.Arguments],
            Env = config.Env.ToDictionary(static pair => pair.Key, static pair => pair.Value),
            EventRules = [.. config.EventRules]
        };
    }

    private static void TryRollback(StorageMutationJournal? storageJournal, string operation)
    {
        if (storageJournal is null)
            return;

        try
        {
            storageJournal.Rollback();
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                "[InstanceUpdateCoordinator] Failed to roll back storage while {Operation}. Recovery data remains at {StagingDirectory}.",
                operation,
                storageJournal.StagingDirectory);
        }
    }

    private static void TryRestoreFilePair(FilePairMutationJournal? journal, string description)
    {
        if (journal is null)
            return;

        try
        {
            journal.Restore();
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                "[InstanceUpdateCoordinator] Failed to restore {Description} transaction journal.",
                description);
        }
    }

    private static void TryDisposeReplacement(IInstance replacement)
    {
        try
        {
            replacement.Dispose();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "[InstanceUpdateCoordinator] Failed to dispose an unpublished replacement instance.");
        }
    }

    private sealed class StorageMutationJournal : IDisposable
    {
        private readonly string _workingDirectory;
        private readonly string _stagingDirectory;
        private readonly List<Entry> _entries = [];
        private readonly List<string> _ownedDirectories = [];
        private bool _finished;

        public StorageMutationJournal(string workingDirectory)
        {
            _workingDirectory = workingDirectory;
            _stagingDirectory = Path.Combine(workingDirectory, ".instance-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_stagingDirectory);
        }

        public void Stage(string path)
        {
            EnsureActive();
            if (_entries.Any(entry => PathsEqual(entry.OriginalPath, path)))
                return;

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                _entries.Add(new Entry(path, null));
                return;
            }

            var stagedPath = Path.Combine(_stagingDirectory, _entries.Count.ToString("D4"));
            if (File.Exists(path))
                File.Move(path, stagedPath, true);
            else
                Directory.Move(path, stagedPath);

            _entries.Add(new Entry(path, stagedPath));
        }

        public string CreateStagedFilePath()
        {
            return Path.Combine(_stagingDirectory, "replacement-" + Guid.NewGuid().ToString("N"));
        }

        public (Guid InstanceId, string WorkingDirectory) CreateFactoryWorkspace()
        {
            Guid instanceId;
            string workspace;
            do
            {
                instanceId = Guid.NewGuid();
                workspace = Path.Combine(FileManager.InstancesRoot, instanceId.ToString());
            } while (Directory.Exists(workspace) || File.Exists(workspace));

            _ownedDirectories.Add(workspace);
            Directory.CreateDirectory(workspace);
            return (instanceId, workspace);
        }

        public void ApplyDirectoryTree(
            string sourceDirectory,
            CancellationToken cancellationToken,
            params string[] excludedRelativePaths)
        {
            EnsureActive();
            cancellationToken.ThrowIfCancellationRequested();
            var exclusions = excludedRelativePaths
                .Select(NormalizeRelativePath)
                .ToHashSet(FactoryWorkspaceNameComparer);
            var (directories, files) = InspectFactoryTree(sourceDirectory, cancellationToken);
            var directoryEntries = directories
                .Select(sourcePath => CreateFactoryEntry(sourceDirectory, sourcePath, exclusions))
                .Where(static entry => entry is not null)
                .Select(static entry => entry!.Value)
                .ToArray();
            var fileEntries = files
                .Select(sourcePath => CreateFactoryEntry(sourceDirectory, sourcePath, exclusions))
                .Where(static entry => entry is not null)
                .Select(static entry => entry!.Value)
                .ToArray();

            foreach (var entry in directoryEntries.Concat(fileEntries))
            {
                if (HasWindowsCaseAlias(_workingDirectory, entry.DestinationPath))
                {
                    throw new IOException(
                        $"Factory output '{entry.RelativePath}' conflicts with Windows path casing.");
                }
            }

            foreach (var entry in directoryEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Directory.Exists(entry.DestinationPath))
                    continue;
                if (File.Exists(entry.DestinationPath))
                {
                    throw new IOException(
                        $"Factory directory '{entry.RelativePath}' conflicts with an existing instance file.");
                }

                Stage(entry.DestinationPath);
                Directory.CreateDirectory(entry.DestinationPath);
            }

            foreach (var entry in fileEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureFactorySourcePathIsSafe(sourceDirectory, entry.SourcePath);
                if (Directory.Exists(entry.DestinationPath))
                {
                    throw new IOException(
                        $"Factory file '{entry.RelativePath}' conflicts with an existing instance directory.");
                }
                Stage(entry.DestinationPath);
                Directory.CreateDirectory(Path.GetDirectoryName(entry.DestinationPath)!);
                File.Move(entry.SourcePath, entry.DestinationPath, true);
            }
        }

        private FactoryTreeEntry? CreateFactoryEntry(
            string sourceDirectory,
            string sourcePath,
            HashSet<string> exclusions)
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            if (exclusions.Contains(NormalizeRelativePath(relativePath)))
                return null;

            return new FactoryTreeEntry(sourcePath, relativePath, ResolveDestination(relativePath));
        }

        private static (string[] Directories, string[] Files) InspectFactoryTree(
            string sourceDirectory,
            CancellationToken cancellationToken)
        {
            EnsureFactoryEntryIsNotReparsePoint(sourceDirectory);
            var directories = new List<string>();
            var files = new List<string>();
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(sourceDirectory);

            while (pendingDirectories.TryPop(out var currentDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureFactorySourcePathIsSafe(sourceDirectory, currentDirectory);
                var entries = Directory.EnumerateFileSystemEntries(currentDirectory).ToArray();
                if (OperatingSystem.IsWindows() && entries
                        .Select(Path.GetFileName)
                        .GroupBy(static name => name, StringComparer.OrdinalIgnoreCase)
                        .Any(static group => group.Distinct(StringComparer.Ordinal).Skip(1).Any()))
                {
                    throw new IOException("Factory output contains case-conflicting Windows paths.");
                }

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var attributes = File.GetAttributes(entry);
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        throw new IOException(
                            $"Factory output '{Path.GetRelativePath(sourceDirectory, entry)}' is a reparse point.");
                    }

                    if (attributes.HasFlag(FileAttributes.Directory))
                    {
                        directories.Add(entry);
                        pendingDirectories.Push(entry);
                    }
                    else
                    {
                        files.Add(entry);
                    }
                }
            }

            return (
                directories
                    .OrderBy(static path => path.Length)
                    .ThenBy(static path => path, FactoryWorkspaceNameComparer)
                    .ThenBy(static path => path, StringComparer.Ordinal)
                    .ToArray(),
                files
                    .OrderBy(static path => path, FactoryWorkspaceNameComparer)
                    .ThenBy(static path => path, StringComparer.Ordinal)
                    .ToArray());
        }

        private static void EnsureFactorySourcePathIsSafe(string sourceDirectory, string sourcePath)
        {
            FileManager.ResolveAndValidatePath(sourcePath, sourceDirectory);
            EnsureFactoryEntryIsNotReparsePoint(sourcePath);
        }

        private static void EnsureFactoryEntryIsNotReparsePoint(string path)
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Factory output reparse points are not permitted.");
        }

        private readonly record struct FactoryTreeEntry(
            string SourcePath,
            string RelativePath,
            string DestinationPath);

        internal string StagingDirectory => _stagingDirectory;

        public void PrepareCommit(string? preservedOriginalPath, List<string> preservedPaths)
        {
            EnsureActive();
            if (preservedOriginalPath is not null)
            {
                var preservedIndex = _entries.FindIndex(entry =>
                    PathsEqual(entry.OriginalPath, preservedOriginalPath));
                if (preservedIndex >= 0)
                {
                    var preservedEntry = _entries[preservedIndex];
                    if (preservedEntry.RollbackPath is null ||
                        (!File.Exists(preservedEntry.RollbackPath) && !Directory.Exists(preservedEntry.RollbackPath)))
                        return;

                    var backupDirectory = Path.Combine(Path.GetDirectoryName(preservedOriginalPath)!, "backup");
                    Directory.CreateDirectory(backupDirectory);
                    var backupPath = Path.Combine(
                        backupDirectory,
                        $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}_{Path.GetFileName(preservedOriginalPath)}");
                    Move(preservedEntry.RollbackPath, backupPath);
                    _entries[preservedIndex] = preservedEntry with { RollbackPath = backupPath };
                    preservedPaths.Add(backupPath);
                }
            }
        }

        public void CompleteCommit()
        {
            if (_finished)
                return;

            _entries.Clear();
            _finished = true;
            var failures = new List<Exception>();
            CaptureFailure(DeleteStagingDirectory, failures);
            DeleteOwnedDirectories(failures);
            ThrowIfCleanupFailed("Failed to clean committed instance storage transaction.", failures);
        }

        public void Rollback()
        {
            if (_finished)
                return;

            var failures = new List<Exception>();
            var restoreFailed = false;
            foreach (var entry in _entries.AsEnumerable().Reverse())
            {
                try
                {
                    Delete(entry.OriginalPath);
                    if (entry.RollbackPath is not null &&
                        (File.Exists(entry.RollbackPath) || Directory.Exists(entry.RollbackPath)))
                    {
                        Move(entry.RollbackPath, entry.OriginalPath);
                    }
                }
                catch (Exception exception)
                {
                    restoreFailed = true;
                    failures.Add(exception);
                }
            }

            _entries.Clear();
            _finished = true;
            if (!restoreFailed)
                CaptureFailure(DeleteStagingDirectory, failures);
            DeleteOwnedDirectories(failures);
            ThrowIfCleanupFailed("Failed to fully roll back instance storage transaction.", failures);
        }

        public void Dispose()
        {
            if (_finished)
                return;

            try
            {
                Rollback();
            }
            catch (Exception exception)
            {
                Log.Error(
                    exception,
                    "[InstanceUpdateCoordinator] Failed to dispose storage transaction {StagingDirectory}.",
                    _stagingDirectory);
            }
        }

        private string ResolveDestination(string relativePath)
        {
            return FileManager.ResolveAndValidatePath(Path.Combine(_workingDirectory, relativePath), _workingDirectory);
        }

        private static string NormalizeRelativePath(string path)
        {
            return path.Replace('\\', '/');
        }

        private void DeleteStagingDirectory()
        {
            if (Directory.Exists(_stagingDirectory))
                Directory.Delete(_stagingDirectory, true);
        }

        private void DeleteOwnedDirectories(List<Exception> failures)
        {
            foreach (var directory in _ownedDirectories)
            {
                try
                {
                    if (Directory.Exists(directory))
                        Directory.Delete(directory, true);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            _ownedDirectories.Clear();
        }

        private void EnsureActive()
        {
            if (_finished)
                throw new InvalidOperationException("The storage transaction has already finished.");
        }

        private static void CaptureFailure(Action action, List<Exception> failures)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        private static void ThrowIfCleanupFailed(string message, List<Exception> failures)
        {
            if (failures.Count > 0)
                throw new AggregateException(message, failures);
        }

        private static void Delete(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, true);
        }

        private static void Move(string source, string destination)
        {
            if (File.Exists(source))
                File.Move(source, destination, true);
            else
                Directory.Move(source, destination);
        }

        private sealed record Entry(string OriginalPath, string? RollbackPath);
    }

    private sealed class FilePairMutationJournal
    {
        private readonly FileSystemEntrySnapshot _primary;
        private readonly FileSystemEntrySnapshot _backup;

        private FilePairMutationJournal(string path)
        {
            _primary = FileSystemEntrySnapshot.Capture(path);
            _backup = FileSystemEntrySnapshot.Capture(path + ".bak");
        }

        public static FilePairMutationJournal Capture(string path)
        {
            return new FilePairMutationJournal(path);
        }

        public void Restore()
        {
            var failures = new List<Exception>();
            try
            {
                _primary.Restore();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                _backup.Restore();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            if (failures.Count > 0)
                throw new AggregateException("Failed to restore a persisted file pair.", failures);
        }

        private sealed class FileSystemEntrySnapshot
        {
            private FileSystemEntrySnapshot(string path, EntryKind kind, byte[]? content)
            {
                Path = path;
                Kind = kind;
                Content = content;
            }

            private string Path { get; }

            private EntryKind Kind { get; }

            private byte[]? Content { get; }

            public static FileSystemEntrySnapshot Capture(string path)
            {
                if (File.Exists(path))
                    return new FileSystemEntrySnapshot(path, EntryKind.File, File.ReadAllBytes(path));

                return Directory.Exists(path)
                    ? new FileSystemEntrySnapshot(path, EntryKind.Directory, null)
                    : new FileSystemEntrySnapshot(path, EntryKind.Missing, null);
            }

            public void Restore()
            {
                switch (Kind)
                {
                    case EntryKind.Missing:
                        DeleteExisting(Path);
                        break;
                    case EntryKind.File:
                        DeleteExisting(Path);
                        File.WriteAllBytes(Path, Content!);
                        break;
                    case EntryKind.Directory:
                        if (File.Exists(Path))
                            File.Delete(Path);
                        Directory.CreateDirectory(Path);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown config journal entry kind '{Kind}'.");
                }
            }

            private static void DeleteExisting(string path)
            {
                if (File.Exists(path))
                    File.Delete(path);
                else if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }

            private enum EntryKind
            {
                Missing,
                File,
                Directory
            }
        }
    }
}
