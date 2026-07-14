using System;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.InstanceConsole.ViewModels.Models;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;
using MCServerLauncher.WPF.View.Components;
using MCServerLauncher.WPF.View.CreateInstanceProvider;
using Microsoft.Win32;
using Serilog;
using System.Windows;
using System.Windows.Controls;
using ListView = iNKORE.UI.WPF.Modern.Controls.ListView;
using TypedDaemonClient = MCServerLauncher.DaemonClient.DaemonClient;
using TypedInstanceCoreReplacementRequest = MCServerLauncher.Common.Contracts.Instances.InstanceCoreReplacementRequest;
using TypedInstanceReference = MCServerLauncher.Common.Contracts.Instances.InstanceReference;
using TypedUpdateInstanceSettingsRequest = MCServerLauncher.Common.Contracts.Instances.UpdateInstanceSettingsRequest;

namespace MCServerLauncher.WPF.InstanceConsole.ViewModels;

public partial class InstanceSettingsViewModel : ObservableObject
{
    private readonly INotificationService _notification;
    private TypedDaemonClient? _daemon;
    private Guid _instanceId;
    private SettingsSnapshot? _originalSnapshot;
    private bool _suppressDirtyTracking;
    private bool _suppressJavaRuntimeTextChanged;

    [ObservableProperty] private InstanceSettingsModel _settings = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isScanningJava;
    [ObservableProperty] private string _instanceIdText = string.Empty;
    [ObservableProperty] private JavaRuntimeOption? _selectedJavaRuntime;
    [ObservableProperty] private string _javaRuntimeDisplayText = string.Empty;
    [ObservableProperty] private bool _javaRuntimeScanCompleted;
    [ObservableProperty] private bool _hasUnsavedChanges;

    public ObservableCollection<JvmArgumentModel> JvmArguments { get; } = [];
    public ObservableCollection<JavaRuntimeOption> JavaRuntimeOptions { get; } = [];
    public bool CanEditJavaRuntime => Settings.CanEdit && JavaRuntimeScanCompleted && !IsScanningJava;

    public InstanceSettingsViewModel(INotificationService notification)
    {
        _notification = notification;
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        JvmArguments.CollectionChanged += OnJvmArgumentsCollectionChanged;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _instanceId = InstanceDataManager.Instance.InstanceId;
            InstanceIdText = _instanceId.ToString();
            _daemon = InstanceDataManager.Instance.CurrentDaemon;

            if (_daemon == null)
            {
                _notification.Push(Lang.Tr["Error"], Lang.Tr["ComponentManager_DaemonUnavailable"], true, InfoBarSeverity.Error);
                return;
            }

            await RefreshAsync();
            _ = LoadJavaRuntimeOptionsAsync(showNotifications: false);
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
            var settingsResult = await _daemon.Instances.GetInstanceSettingsAsync(new TypedInstanceReference(_instanceId), default);
            if (settingsResult.IsErr(out var settingsError))
                throw DaemonErrorLocalization.ToException(settingsError!);

            var result = settingsResult.Unwrap();
            _suppressDirtyTracking = true;
            Settings = new InstanceSettingsModel
            {
                Name = result.Config.Name,
                JavaPath = result.Config.JavaPath,
                Version = result.Config.Version,
                Target = result.Config.Target,
                InstanceType = result.Config.InstanceType,
                Arguments = result.Config.Arguments.ToArray(),
                WorkingDirectory = result.WorkingDirectory,
                CanEdit = result.CanEdit,
                EditBlockedReason = result.EditBlockedReason ?? string.Empty,
                CurrentTargetExists = result.CurrentTargetExists,
                InstallMetadata = result.InstallMetadata is null
                    ? null
                    : new Common.ProtoType.Action.InstanceInstallMetadata
                    {
                        InstallerKind = result.InstallMetadata.InstallerKind,
                        InstallerSourcePath = result.InstallMetadata.InstallerSourcePath,
                        GeneratedPaths = result.InstallMetadata.GeneratedPaths.ToArray(),
                        ResolvedLaunchTarget = result.InstallMetadata.ResolvedLaunchTarget,
                        InstalledAt = result.InstallMetadata.InstalledAt
                    }
            };
            ResetJvmArguments(result.Config.Arguments.ToArray());
            JavaRuntimeDisplayText = Settings.JavaPath;
            SelectJavaRuntimeOptionByPath(Settings.JavaPath);
            _originalSnapshot = CreateSnapshot();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstanceSettings] Failed to refresh settings");
            _notification.Push(Lang.Tr["Error"], ex.Message, true, InfoBarSeverity.Error);
        }
        finally
        {
            _suppressDirtyTracking = false;
            UpdateHasUnsavedChanges();
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ScanJavaAsync()
    {
        await LoadJavaRuntimeOptionsAsync(showNotifications: true);
    }

    [RelayCommand]
    private void AddJvmArgument()
    {
        AddJvmArgumentItem(string.Empty);
    }

    [RelayCommand]
    private void RemoveJvmArgument(JvmArgumentModel? item)
    {
        if (item == null) return;

        item.PropertyChanged -= OnJvmArgumentPropertyChanged;
        JvmArguments.Remove(item);
    }

    [RelayCommand]
    private async Task ShowJvmArgumentHelperAsync()
    {
        (ContentDialog dialog, global::MCServerLauncher.WPF.View.Components.CreateInstance.JvmArgHelper argHelper) =
            await Utils.ConstructJvmArgHelperDialog();
        dialog.PrimaryButtonClick += (_, _) => AddJvmArguments(argHelper.GetArgs());
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception)
        {
            // Dialog can throw if another ContentDialog is already open.
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
            Settings.Arguments = GetJvmArguments();
            if (!ValidateBeforeSave())
            {
                return;
            }

            TypedInstanceCoreReplacementRequest? replacement = null;
            if (!string.IsNullOrWhiteSpace(Settings.ReplacementCorePath))
            {
                var uploadPath = $"/instances/{_instanceId}/uploads/{Path.GetFileName(Settings.ReplacementCorePath)}";
                await using var stream = File.OpenRead(Settings.ReplacementCorePath);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                stream.Position = 0;
                var openResult = await _daemon.Files.OpenUploadAsync(
                    new UploadOpenRequest(uploadPath, stream.Length, hash),
                    default);
                if (openResult.IsErr(out var openError))
                    throw DaemonErrorLocalization.ToException(openError!);

                var session = openResult.Unwrap();
                var completed = false;
                try
                {
                    var buffer = new byte[session.MaxChunkSize];
                    var offset = 0L;
                    int read;
                    while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
                    {
                        var writeResult = await _daemon.Files.WriteUploadChunkAsync(
                            new UploadChunkRequest(session.SessionId, offset, ImmutableArray.Create(buffer[..read])),
                            default);
                        if (writeResult.IsErr(out var writeError))
                        throw DaemonErrorLocalization.ToException(writeError!);

                        offset += read;
                    }

                    var closeResult = await _daemon.Files.CloseUploadAsync(session.SessionId, default);
                    if (closeResult.IsErr(out var closeError))
                    throw DaemonErrorLocalization.ToException(closeError!);

                    completed = true;
                }
                finally
                {
                    if (!completed)
                    {
                        var cancelResult = await _daemon.Files.CancelUploadAsync(session.SessionId, default);
                        if (cancelResult.IsErr(out var cancelError))
                            Log.Warning("[InstanceSettings] Failed to cancel upload {0}: {1}", session.SessionId, cancelError!.Message);
                    }
                }

                replacement = new TypedInstanceCoreReplacementRequest(
                    uploadPath,
                    Path.GetFileName(Settings.ReplacementCorePath));
            }

            var updateResult = await _daemon.Instances.UpdateInstanceSettingsAsync(
                new TypedUpdateInstanceSettingsRequest(
                    _instanceId,
                    Settings.Name,
                    Settings.InstanceType,
                    Settings.JavaPath,
                    Settings.Arguments.ToImmutableArray(),
                    Settings.Version,
                    replacement,
                    Settings.ForceRerunInstaller),
                default);
            if (updateResult.IsErr(out var updateError))
                throw DaemonErrorLocalization.ToException(updateError!);

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

    private bool ValidateBeforeSave()
    {
        if (!CreateInstanceValidation.TryValidateInstanceName(Settings.Name, out var nameError))
        {
            PushValidationError(nameError);
            return false;
        }

        if (!CreateInstanceValidation.TryValidateJavaPath(Settings.JavaPath, out var javaPathError))
        {
            PushValidationError(javaPathError);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(Settings.ReplacementCorePath)
            && !CreateInstanceValidation.TryValidateLocalJarPath(Settings.ReplacementCorePath, out var corePathError))
        {
            PushValidationError(corePathError);
            return false;
        }

        return true;
    }

    private void PushValidationError(string message)
    {
        _notification.Push(Lang.Tr["Error"], message, true, InfoBarSeverity.Error);
    }

    partial void OnSettingsChanging(InstanceSettingsModel value)
    {
        value.PropertyChanged -= OnSettingsPropertyChanged;
    }

    partial void OnSettingsChanged(InstanceSettingsModel value)
    {
        value.PropertyChanged += OnSettingsPropertyChanged;
        UpdateHasUnsavedChanges();
    }

    partial void OnSelectedJavaRuntimeChanged(JavaRuntimeOption? value)
    {
        if (value == null) return;
        Settings.JavaPath = value.Path;
        SetJavaRuntimeDisplayText(value.Path);
    }

    partial void OnJavaRuntimeDisplayTextChanged(string value)
    {
        if (_suppressJavaRuntimeTextChanged) return;

        var selected = JavaRuntimeOptions.FirstOrDefault(option =>
            string.Equals(option.DisplayName, value, StringComparison.Ordinal)
            || string.Equals(option.Path, value, StringComparison.OrdinalIgnoreCase));

        Settings.JavaPath = selected?.Path ?? value;
        if (!Equals(SelectedJavaRuntime, selected))
        {
            SelectedJavaRuntime = selected;
        }
    }

    partial void OnIsScanningJavaChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditJavaRuntime));
    }

    partial void OnJavaRuntimeScanCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditJavaRuntime));
    }

    private async Task LoadJavaRuntimeOptionsAsync(bool showNotifications)
    {
        if (_daemon == null || IsScanningJava) return;

        IsScanningJava = true;
        if (showNotifications)
        {
            _notification.Push(Lang.Tr["PleaseWait"], Lang.Tr["SearchingJvmTip"], false, InfoBarSeverity.Informational);
        }

        try
        {
            var javaResult = await _daemon.System.ListJavaRuntimesAsync(default);
            if (javaResult.IsErr(out var javaError))
                throw DaemonErrorLocalization.ToException(javaError!);

            var javaRuntimeOptions = javaResult.Unwrap().Items.ToArray();
            PopulateJavaRuntimeOptions(javaRuntimeOptions);

            if (!showNotifications) return;

            if (javaRuntimeOptions.Length == 0)
            {
                _notification.Push(Lang.Tr["Info"], Lang.Tr["NoJavaFound"], true, InfoBarSeverity.Warning);
                return;
            }

            await ShowJavaRuntimePickerAsync(javaRuntimeOptions);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstanceSettings] Failed to scan Java runtimes");
            if (showNotifications)
            {
                _notification.Push(Lang.Tr["Error"], $"{Lang.Tr["SearchJavaError"]}: {ex.Message}", true, InfoBarSeverity.Error);
            }
        }
        finally
        {
            JavaRuntimeScanCompleted = true;
            IsScanningJava = false;
        }
    }

    private async Task ShowJavaRuntimePickerAsync(JavaRuntime[] jvms)
    {
        var listView = new ListView
        {
            ItemsSource = JavaRuntimeOptions,
            SelectedIndex = 0,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var scroll = new ScrollViewerEx
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 500,
            Content = listView
        };

        var dialog = new ContentDialog
        {
            Title = Lang.Tr["PleaseSelectJvm"],
            PrimaryButtonText = Lang.Tr["Continue"],
            SecondaryButtonText = Lang.Tr["Cancel"],
            DefaultButton = ContentDialogButton.Primary,
            FullSizeDesired = false,
            Content = scroll
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary
            && listView.SelectedIndex >= 0
            && listView.SelectedIndex < jvms.Length)
        {
            Settings.JavaPath = jvms[listView.SelectedIndex].Path;
            SelectJavaRuntimeOptionByPath(Settings.JavaPath);
        }
    }

    private void PopulateJavaRuntimeOptions(JavaRuntime[] jvms)
    {
        JavaRuntimeOptions.Clear();
        foreach (var jvm in jvms)
        {
            JavaRuntimeOptions.Add(new JavaRuntimeOption(
                $"({jvm.Version}, {jvm.Architecture}) {jvm.Path}",
                jvm.Path));
        }

        SelectJavaRuntimeOptionByPath(Settings.JavaPath);
    }

    private void SelectJavaRuntimeOptionByPath(string path)
    {
        var selected = JavaRuntimeOptions.FirstOrDefault(option =>
            string.Equals(option.Path, path, StringComparison.OrdinalIgnoreCase));
        SelectedJavaRuntime = selected;
        SetJavaRuntimeDisplayText(selected?.Path ?? path);
    }

    private void SetJavaRuntimeDisplayText(string value)
    {
        _suppressJavaRuntimeTextChanged = true;
        JavaRuntimeDisplayText = value;
        _suppressJavaRuntimeTextChanged = false;
    }

    private void ResetJvmArguments(string[] arguments)
    {
        foreach (var item in JvmArguments)
        {
            item.PropertyChanged -= OnJvmArgumentPropertyChanged;
        }

        JvmArguments.Clear();
        foreach (var argument in arguments.Where(arg => !string.IsNullOrWhiteSpace(arg)))
        {
            AddJvmArgumentItem(argument);
        }
    }

    private void AddJvmArguments(string[]? arguments)
    {
        if (arguments == null) return;

        var blankItems = JvmArguments.Where(item => string.IsNullOrWhiteSpace(item.Argument)).ToArray();
        foreach (var item in blankItems)
        {
            item.PropertyChanged -= OnJvmArgumentPropertyChanged;
            JvmArguments.Remove(item);
        }

        foreach (var argument in arguments.Where(arg => !string.IsNullOrWhiteSpace(arg)))
        {
            AddJvmArgumentItem(argument);
        }
    }

    private void AddJvmArgumentItem(string argument)
    {
        var item = new JvmArgumentModel { Argument = argument };
        item.PropertyChanged += OnJvmArgumentPropertyChanged;
        JvmArguments.Add(item);
    }

    private string[] GetJvmArguments()
    {
        return JvmArguments
            .Select(item => item.Argument)
            .Where(argument => !string.IsNullOrWhiteSpace(argument))
            .ToArray();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstanceSettingsModel.JavaPath))
        {
            SelectJavaRuntimeOptionByPath(Settings.JavaPath);
        }

        if (e.PropertyName == nameof(InstanceSettingsModel.CanEdit))
        {
            OnPropertyChanged(nameof(CanEditJavaRuntime));
        }

        UpdateHasUnsavedChanges();
    }

    private void OnJvmArgumentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateHasUnsavedChanges();
    }

    private void OnJvmArgumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateHasUnsavedChanges();
    }

    private SettingsSnapshot CreateSnapshot()
    {
        return new SettingsSnapshot(
            Settings.Name,
            Settings.JavaPath,
            Settings.Version,
            Settings.Target,
            Settings.InstanceType,
            GetJvmArguments(),
            Settings.ReplacementCorePath,
            Settings.ForceRerunInstaller);
    }

    private void UpdateHasUnsavedChanges()
    {
        if (_suppressDirtyTracking)
        {
            return;
        }

        HasUnsavedChanges = _originalSnapshot != null && !CreateSnapshot().Equals(_originalSnapshot);
    }

    public sealed record JavaRuntimeOption(string DisplayName, string Path)
    {
        public override string ToString() => DisplayName;
    }

    private sealed class SettingsSnapshot : IEquatable<SettingsSnapshot>
    {
        public SettingsSnapshot(
            string name,
            string javaPath,
            string version,
            string target,
            InstanceType instanceType,
            string[] arguments,
            string replacementCorePath,
            bool forceRerunInstaller)
        {
            Name = name;
            JavaPath = javaPath;
            Version = version;
            Target = target;
            InstanceType = instanceType;
            Arguments = arguments;
            ReplacementCorePath = replacementCorePath;
            ForceRerunInstaller = forceRerunInstaller;
        }

        private string Name { get; }
        private string JavaPath { get; }
        private string Version { get; }
        private string Target { get; }
        private InstanceType InstanceType { get; }
        private string[] Arguments { get; }
        private string ReplacementCorePath { get; }
        private bool ForceRerunInstaller { get; }

        public bool Equals(SettingsSnapshot? other)
        {
            return other != null
                   && Name == other.Name
                   && JavaPath == other.JavaPath
                   && Version == other.Version
                   && Target == other.Target
                   && InstanceType == other.InstanceType
                   && Arguments.SequenceEqual(other.Arguments)
                   && ReplacementCorePath == other.ReplacementCorePath
                   && ForceRerunInstaller == other.ForceRerunInstaller;
        }

        public override int GetHashCode()
        {
            var hash = HashCode.Combine(Name, JavaPath, Version, Target, InstanceType, ReplacementCorePath, ForceRerunInstaller);
            foreach (var argument in Arguments)
            {
                hash = HashCode.Combine(hash, argument);
            }

            return hash;
        }
    }
}
