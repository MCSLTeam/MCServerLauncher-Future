using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using Serilog;

namespace MCServerLauncher.Daemon.Management;

internal sealed class InstanceUpdateCoordinator
{
    private readonly Func<InstanceConfig, IInstance> _instanceFactory;
    private readonly InstanceManager _instanceManager;

    public InstanceUpdateCoordinator(
        InstanceManager instanceManager,
        Func<InstanceConfig, IInstance> instanceFactory)
    {
        _instanceManager = instanceManager;
        _instanceFactory = instanceFactory;
    }

    public Result<GetInstanceSettingsResult, Error> GetInstanceSettings(Guid instanceId)
    {
        if (!_instanceManager.Instances.TryGetValue(instanceId, out var instance))
        {
            return ResultExt.Err<GetInstanceSettingsResult>(new Error($"Instance {instanceId} not found"));
        }

        var config = CloneConfig(instance.Config);
        var workingDirectory = config.GetWorkingDirectory();
        var currentTargetExists = InstanceTargetPathValidator.TryResolveTargetFile(
            workingDirectory,
            config.Target,
            out var currentTargetPath,
            out _) && File.Exists(currentTargetPath);
        var metadata = InstanceInstallMetadataStore.Read(workingDirectory);
        var canEdit = instance.Status is InstanceStatus.Stopped or InstanceStatus.Crashed;

        return ResultExt.Ok(new GetInstanceSettingsResult
        {
            Config = config,
            WorkingDirectory = workingDirectory,
            CurrentTargetExists = currentTargetExists,
            CanEdit = canEdit,
            EditBlockedReason = canEdit ? null : $"Instance is {instance.Status}",
            InstallMetadata = metadata
        });
    }

    public async Task<Result<UpdateInstanceSettingsResult, Error>> UpdateInstanceSettings(
        UpdateInstanceSettingsParameter request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var mutation = await _instanceManager.AcquireInstanceMutationAsync(request.Id, ct);
        if (!_instanceManager.Instances.TryGetValue(request.Id, out var instance))
        {
            return ResultExt.Err<UpdateInstanceSettingsResult>(new Error($"Instance {request.Id} not found"));
        }

        if (instance.Status is not InstanceStatus.Stopped and not InstanceStatus.Crashed)
        {
            return ResultExt.Err<UpdateInstanceSettingsResult>(new Error($"Instance {request.Id} must be stopped before updating settings"));
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
        var generatedPaths = Array.Empty<(string RelativePath, string FullPath)>();

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

            replacementSourcePath = FileManager.ResolveAndValidatePath(request.ReplacementCore.UploadedSourcePath);
            if (!File.Exists(replacementSourcePath))
            {
                return ResultExt.Err<UpdateInstanceSettingsResult>(new Error($"Replacement core not found at {request.ReplacementCore.UploadedSourcePath}"));
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
            requiresRestart = true;
        }

        var newConfig = currentConfig with
        {
            Name = request.Name,
            InstanceType = instanceType,
            JavaPath = request.JavaPath ?? currentConfig.JavaPath,
            Arguments = request.Arguments,
            Version = version,
            Target = target
        };

        var validation = newConfig.ValidateConfig();
        if (validation.IsErr(out var validationError))
        {
            return ResultExt.Err<UpdateInstanceSettingsResult>(validationError!);
        }

        var installMetadata = InstanceInstallMetadataStore.Read(workingDirectory);
        var shouldRerunInstaller = request.ForceRerunInstaller || RequiresInstaller(instanceType);

        if (request.ReplacementCore is not null && shouldRerunInstaller && installMetadata is not null)
        {
            var validatedGeneratedPaths = new List<(string RelativePath, string FullPath)>();
            foreach (var generatedPath in installMetadata.GeneratedPaths ?? [])
            {
                if (!InstanceTargetPathValidator.TryResolveGeneratedPath(
                        workingDirectory,
                        generatedPath,
                        out var fullGeneratedPath,
                        out var generatedPathError))
                {
                    return ResultExt.Err<UpdateInstanceSettingsResult>(generatedPathError!);
                }

                validatedGeneratedPaths.Add((generatedPath, fullGeneratedPath));
            }

            generatedPaths = [.. validatedGeneratedPaths];
        }

        if (request.ReplacementCore is not null)
        {
            storageJournal = new StorageMutationJournal(workingDirectory);
            try
            {
                storageJournal.Stage(originalTargetPath!);
                storageJournal.Stage(replacementTargetPath!);

                if (shouldRerunInstaller)
                {
                    storageJournal.Stage(InstanceInstallMetadataStore.GetPath(workingDirectory));
                    foreach (var (generatedPath, fullGeneratedPath) in generatedPaths)
                    {
                        storageJournal.Stage(fullGeneratedPath);
                        deletedGeneratedPaths.Add(generatedPath);
                    }
                }
                else
                {
                    stagedReplacementCore = storageJournal.CreateStagedFilePath();
                    File.Copy(replacementSourcePath!, stagedReplacementCore, true);
                }
            }
            catch (Exception exception)
            {
                storageJournal.Rollback();
                return ResultExt.Err<UpdateInstanceSettingsResult>(
                    new Error("Failed to stage instance core replacement").CauseBy(exception));
            }

            if (ct.IsCancellationRequested)
            {
                storageJournal.Rollback();
                ct.ThrowIfCancellationRequested();
            }
        }

        if (request.ReplacementCore != null && shouldRerunInstaller)
        {
            var setting = new InstanceFactorySetting
            {
                Name = newConfig.Name,
                Target = newConfig.Target,
                TargetType = newConfig.TargetType,
                InstanceType = newConfig.InstanceType,
                JavaPath = newConfig.JavaPath,
                Arguments = newConfig.Arguments,
                Version = newConfig.Version,
                Source = replacementSourcePath!,
                SourceType = SourceType.Core,
                Mirror = InstanceFactoryMirror.None,
                UsePostProcess = false,
                Uuid = newConfig.Uuid,
                Env = newConfig.Env,
                EventRules = newConfig.EventRules,
                InputEncoding = newConfig.InputEncoding,
                OutputEncoding = newConfig.OutputEncoding
            };

            Result<InstanceConfig, Error> factoryResult;
            try
            {
                factoryResult = await setting.ApplyInstanceFactory();
            }
            catch
            {
                storageJournal!.Rollback();
                throw;
            }
            if (ct.IsCancellationRequested)
            {
                storageJournal!.Rollback();
                ct.ThrowIfCancellationRequested();
            }
            if (factoryResult.IsErr(out var installError))
            {
                storageJournal!.Rollback();
                return ResultExt.Err<UpdateInstanceSettingsResult>(new Error("Failed to rerun installer").WithInner(installError));
            }

            newConfig = factoryResult.Unwrap() with
            {
                Name = newConfig.Name,
                Uuid = newConfig.Uuid,
                Env = newConfig.Env,
                EventRules = newConfig.EventRules,
                InputEncoding = newConfig.InputEncoding,
                OutputEncoding = newConfig.OutputEncoding
            };

            try
            {
                InstanceInstallMetadataStore.Write(workingDirectory, new InstanceInstallMetadata
                {
                    InstallerKind = newConfig.InstanceType.ToString(),
                    InstallerSourcePath = replacementSourcePath,
                    GeneratedPaths = DetectGeneratedPaths(workingDirectory),
                    ResolvedLaunchTarget = newConfig.Target,
                    InstalledAt = DateTimeOffset.UtcNow
                });
            }
            catch (Exception exception)
            {
                storageJournal!.Rollback();
                return ResultExt.Err<UpdateInstanceSettingsResult>(
                    new Error("Failed to persist instance installation metadata").CauseBy(exception));
            }

            reinstalled = true;
        }
        IInstance replacement;
        try
        {
            replacement = _instanceFactory(newConfig);
        }
        catch (Exception exception)
        {
            storageJournal?.Rollback();
            return ResultExt.Err<UpdateInstanceSettingsResult>(
                new Error("Failed to construct updated instance").CauseBy(exception));
        }

        var configPath = Path.Combine(workingDirectory, InstanceConfig.FileName);
        var configJournal = ConfigMutationJournal.Capture(configPath);
        try
        {
            FileManager.WriteJsonAndBackup(configPath, newConfig);
        }
        catch (Exception exception)
        {
            TryDisposeReplacement(replacement);
            storageJournal?.Rollback();
            TryRestoreConfig(configJournal);
            return ResultExt.Err<UpdateInstanceSettingsResult>(
                new Error("Failed to persist instance settings").CauseBy(exception));
        }

        try
        {
            if (stagedReplacementCore is not null)
            {
                File.Move(stagedReplacementCore, replacementTargetPath!, true);
            }

            storageJournal?.PrepareCommit(originalTargetPath, preservedPaths);
        }
        catch (Exception exception)
        {
            TryDisposeReplacement(replacement);
            storageJournal?.Rollback();
            TryRestoreConfig(configJournal);
            return ResultExt.Err<UpdateInstanceSettingsResult>(
                new Error("Failed to commit instance settings").CauseBy(exception));
        }

        try
        {
            _instanceManager.ReplaceInstance(request.Id, replacement);
        }
        catch (Exception exception)
        {
            TryDisposeReplacement(replacement);
            storageJournal?.Rollback();
            TryRestoreConfig(configJournal);
            return ResultExt.Err<UpdateInstanceSettingsResult>(
                new Error("Failed to commit updated instance").CauseBy(exception));
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
                    request.Id,
                    storageJournal.StagingDirectory);
            }
        }

        return ResultExt.Ok(new UpdateInstanceSettingsResult
        {
            Config = newConfig,
            RequiresRestart = requiresRestart,
            Reinstalled = reinstalled,
            DeletedGeneratedPaths = deletedGeneratedPaths.ToArray(),
            PreservedOriginalPaths = preservedPaths.ToArray()
        });
    }

    private static bool RequiresInstaller(InstanceType instanceType)
    {
        return instanceType is InstanceType.MCForge or InstanceType.MCNeoForge or InstanceType.MCCleanroom;
    }

    private static string[] DetectGeneratedPaths(string workingDirectory)
    {
        var generated = new List<string>();

        foreach (var relativePath in new[] { "libraries", "run.bat", "run.sh", "unix_args.txt", "user_jvm_args.txt", "server.jar" })
        {
            var fullPath = Path.Combine(workingDirectory, relativePath);
            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                generated.Add(relativePath);
            }
        }

        return generated.ToArray();
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

    private static void TryRestoreConfig(ConfigMutationJournal configJournal)
    {
        try
        {
            configJournal.Restore();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[InstanceUpdateCoordinator] Failed to restore instance settings transaction journal");
        }
    }

    private static void TryDisposeReplacement(IInstance replacement)
    {
        try
        {
            replacement.Dispose();
        }
        catch
        {
            // The failed replacement was never published; preserve the original command failure.
        }
    }

    private sealed class StorageMutationJournal : IDisposable
    {
        private readonly string _stagingDirectory;
        private readonly List<Entry> _entries = [];

        public StorageMutationJournal(string workingDirectory)
        {
            _stagingDirectory = Path.Combine(workingDirectory, ".instance-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_stagingDirectory);
        }

        public void Stage(string path)
        {
            if (_entries.Any(entry => string.Equals(entry.OriginalPath, path, StringComparison.OrdinalIgnoreCase)))
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

        internal string StagingDirectory => _stagingDirectory;

        public void PrepareCommit(string? preservedOriginalPath, List<string> preservedPaths)
        {
            if (preservedOriginalPath is not null)
            {
                var preservedIndex = _entries.FindIndex(entry =>
                    string.Equals(entry.OriginalPath, preservedOriginalPath, StringComparison.OrdinalIgnoreCase));
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
            _entries.Clear();
            DeleteStagingDirectory();
        }

        public void Rollback()
        {
            foreach (var entry in _entries.AsEnumerable().Reverse())
            {
                Delete(entry.OriginalPath);
                if (entry.RollbackPath is not null &&
                    (File.Exists(entry.RollbackPath) || Directory.Exists(entry.RollbackPath)))
                    Move(entry.RollbackPath, entry.OriginalPath);
            }

            DeleteStagingDirectory();
        }

        public void Dispose()
        {
            DeleteStagingDirectory();
        }

        private void DeleteStagingDirectory()
        {
            if (Directory.Exists(_stagingDirectory))
                Directory.Delete(_stagingDirectory, true);
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

    private sealed class ConfigMutationJournal
    {
        private readonly FileSystemEntrySnapshot _config;
        private readonly FileSystemEntrySnapshot _backup;

        private ConfigMutationJournal(string configPath)
        {
            _config = FileSystemEntrySnapshot.Capture(configPath);
            _backup = FileSystemEntrySnapshot.Capture(configPath + ".bak");
        }

        public static ConfigMutationJournal Capture(string configPath)
        {
            return new ConfigMutationJournal(configPath);
        }

        public void Restore()
        {
            _config.Restore();
            _backup.Restore();
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
