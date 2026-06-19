using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;

namespace MCServerLauncher.Daemon.Management;

internal sealed class InstanceUpdateCoordinator
{
    private readonly InstanceManager _instanceManager;

    public InstanceUpdateCoordinator(InstanceManager instanceManager)
    {
        _instanceManager = instanceManager;
    }

    public Result<GetInstanceSettingsResult, Error> GetInstanceSettings(Guid instanceId)
    {
        if (!_instanceManager.Instances.TryGetValue(instanceId, out var instance))
        {
            return ResultExt.Err<GetInstanceSettingsResult>(new Error($"Instance {instanceId} not found"));
        }

        var config = instance.Config;
        var workingDirectory = config.GetWorkingDirectory();
        var currentTargetPath = Path.Combine(workingDirectory, config.Target);
        var metadata = InstanceInstallMetadataStore.Read(workingDirectory);
        var canEdit = instance.Status is InstanceStatus.Stopped or InstanceStatus.Crashed;

        return ResultExt.Ok(new GetInstanceSettingsResult
        {
            Config = config,
            WorkingDirectory = workingDirectory,
            CurrentTargetExists = File.Exists(currentTargetPath),
            CanEdit = canEdit,
            EditBlockedReason = canEdit ? null : $"Instance is {instance.Status}",
            InstallMetadata = metadata
        });
    }

    public async Task<Result<UpdateInstanceSettingsResult, Error>> UpdateInstanceSettings(UpdateInstanceSettingsParameter request)
    {
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

        if (request.ReplacementCore != null)
        {
            replacementSourcePath = FileManager.ResolveAndValidatePath(request.ReplacementCore.UploadedSourcePath);
            if (!File.Exists(replacementSourcePath))
            {
                return ResultExt.Err<UpdateInstanceSettingsResult>(new Error($"Replacement core not found at {request.ReplacementCore.UploadedSourcePath}"));
            }

            var currentTargetPath = Path.Combine(workingDirectory, currentConfig.Target);
            if (File.Exists(currentTargetPath))
            {
                var backupDirectory = Path.Combine(workingDirectory, "backup");
                Directory.CreateDirectory(backupDirectory);
                var backupPath = Path.Combine(backupDirectory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{currentConfig.Target}");
                File.Move(currentTargetPath, backupPath, true);
                preservedPaths.Add(backupPath);
            }

            target = request.ReplacementCore.PreferredTargetName ?? Path.GetFileName(replacementSourcePath);
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

        var installMetadata = InstanceInstallMetadataStore.Read(workingDirectory);
        var shouldRerunInstaller = request.ForceRerunInstaller || RequiresInstaller(instanceType);

        if (request.ReplacementCore != null && shouldRerunInstaller)
        {
            foreach (var generatedPath in installMetadata?.GeneratedPaths ?? [])
            {
                try
                {
                    var fullGeneratedPath = Path.Combine(workingDirectory, generatedPath);
                    if (File.Exists(fullGeneratedPath))
                    {
                        File.Delete(fullGeneratedPath);
                        deletedGeneratedPaths.Add(generatedPath);
                    }
                    else if (Directory.Exists(fullGeneratedPath))
                    {
                        Directory.Delete(fullGeneratedPath, true);
                        deletedGeneratedPaths.Add(generatedPath);
                    }
                }
                catch
                {
                    // best effort cleanup; installer rerun will recreate as needed
                }
            }

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

            var factoryResult = await setting.ApplyInstanceFactory();
            if (factoryResult.IsErr(out var installError))
            {
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

            InstanceInstallMetadataStore.Write(workingDirectory, new InstanceInstallMetadata
            {
                InstallerKind = newConfig.InstanceType.ToString(),
                InstallerSourcePath = replacementSourcePath,
                GeneratedPaths = DetectGeneratedPaths(workingDirectory),
                ResolvedLaunchTarget = newConfig.Target,
                InstalledAt = DateTimeOffset.UtcNow
            });

            reinstalled = true;
        }
        else if (request.ReplacementCore != null)
        {
            var destinationPath = Path.Combine(workingDirectory, newConfig.Target);
            File.Copy(replacementSourcePath!, destinationPath, true);
        }

        var validation = newConfig.ValidateConfig();
        if (validation.IsErr(out var validationError))
        {
            return ResultExt.Err<UpdateInstanceSettingsResult>(validationError!);
        }

        var configPath = Path.Combine(workingDirectory, InstanceConfig.FileName);
        FileManager.WriteJsonAndBackup(configPath, newConfig);

        var replacement = newConfig.CreateInstance();
        _instanceManager.ReplaceInstance(request.Id, replacement);

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
}
