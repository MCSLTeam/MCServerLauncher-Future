using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.InstanceConsole.ViewModels.Models;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;
using Microsoft.Win32;
using Serilog;

namespace MCServerLauncher.WPF.InstanceConsole.ViewModels;

public partial class InstanceSettingsViewModel : ObservableObject
{
    private readonly INotificationService _notification;
    private IDaemon? _daemon;
    private Guid _instanceId;

    [ObservableProperty] private InstanceSettingsModel _settings = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSaving;

    public InstanceSettingsViewModel(INotificationService notification)
    {
        _notification = notification;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _instanceId = InstanceDataManager.Instance.InstanceId;
            var daemonField = typeof(InstanceDataManager).GetField("_daemon", BindingFlags.NonPublic | BindingFlags.Instance);
            _daemon = daemonField?.GetValue(InstanceDataManager.Instance) as IDaemon;

            if (_daemon == null)
            {
                _notification.Push(Lang.Tr["Error"], Lang.Tr["ComponentManager_DaemonUnavailable"], true, InfoBarSeverity.Error);
                return;
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstanceSettings] Failed to initialize");
            _notification.Push(Lang.Tr["Error"], ex.Message, true, InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_daemon == null || IsLoading) return;

        IsLoading = true;
        try
        {
            var result = await _daemon.GetInstanceSettingsAsync(_instanceId);
            Settings = new InstanceSettingsModel
            {
                Name = result.Config.Name,
                JavaPath = result.Config.JavaPath,
                Version = result.Config.Version,
                Target = result.Config.Target,
                InstanceType = result.Config.InstanceType,
                Arguments = result.Config.Arguments,
                WorkingDirectory = result.WorkingDirectory,
                CanEdit = result.CanEdit,
                EditBlockedReason = result.EditBlockedReason ?? string.Empty,
                CurrentTargetExists = result.CurrentTargetExists,
                InstallMetadata = result.InstallMetadata
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstanceSettings] Failed to refresh settings");
            _notification.Push(Lang.Tr["Error"], ex.Message, true, InfoBarSeverity.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectReplacementCore()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = false,
            Filter = "Java Archive (*.jar)|*.jar|Archive/Executable (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            Settings.ReplacementCorePath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void ClearReplacementCore()
    {
        Settings.ReplacementCorePath = string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_daemon == null || IsSaving) return;
        if (!Settings.CanEdit)
        {
            _notification.Push(Lang.Tr["Warning"], Settings.EditBlockedReason, true, InfoBarSeverity.Warning);
            return;
        }

        IsSaving = true;
        try
        {
            InstanceCoreReplacementRequest? replacement = null;
            if (!string.IsNullOrWhiteSpace(Settings.ReplacementCorePath))
            {
                var uploadPath = $"/instances/{_instanceId}/uploads/{Path.GetFileName(Settings.ReplacementCorePath)}";
                var ctx = await _daemon.UploadFileAsync(Settings.ReplacementCorePath, uploadPath, 1024 * 1024);
                if (ctx.NetworkLoadTask != null) await ctx.NetworkLoadTask;

                replacement = new InstanceCoreReplacementRequest
                {
                    UploadedSourcePath = uploadPath,
                    PreferredTargetName = Path.GetFileName(Settings.ReplacementCorePath)
                };
            }

            var result = await _daemon.UpdateInstanceSettingsAsync(new UpdateInstanceSettingsParameter
            {
                Id = _instanceId,
                Name = Settings.Name,
                InstanceType = Settings.InstanceType,
                JavaPath = Settings.JavaPath,
                Arguments = Settings.Arguments,
                Version = Settings.Version,
                ReplacementCore = replacement,
                ForceRerunInstaller = Settings.ForceRerunInstaller
            });

            _notification.Push(Lang.Tr["Success"], Lang.Tr["SettingsSaveSuccess"], false, InfoBarSeverity.Success);
            Settings.ReplacementCorePath = string.Empty;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstanceSettings] Failed to save settings");
            _notification.Push(Lang.Tr["Error"], ex.Message, true, InfoBarSeverity.Error);
        }
        finally
        {
            IsSaving = false;
        }
    }
}
