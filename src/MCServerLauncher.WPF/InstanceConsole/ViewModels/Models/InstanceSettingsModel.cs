using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.WPF.InstanceConsole.ViewModels.Models;

public partial class InstanceSettingsModel : ObservableObject
{
    private static readonly IReadOnlyList<InstanceType> MinecraftJavaRuntimeTypes =
        Enum.GetValues<InstanceType>()
            .Where(type => type.IsMinecraftJavaRuntimeType())
            .ToArray();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _javaPath = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string _target = string.Empty;
    [ObservableProperty] private InstanceType _instanceType;
    [ObservableProperty] private string[] _arguments = Array.Empty<string>();
    [ObservableProperty] private string _workingDirectory = string.Empty;
    [ObservableProperty] private bool _canEdit;
    [ObservableProperty] private string _editBlockedReason = string.Empty;
    [ObservableProperty] private bool _currentTargetExists;
    [ObservableProperty] private string _replacementCorePath = string.Empty;
    [ObservableProperty] private bool _forceRerunInstaller;
    [ObservableProperty] private InstanceInstallMetadata? _installMetadata;

    public IReadOnlyList<InstanceType> EditableInstanceTypes =>
        MinecraftJavaRuntimeTypes.Contains(InstanceType)
            ? MinecraftJavaRuntimeTypes
            : [InstanceType, .. MinecraftJavaRuntimeTypes];

    public bool HasReplacementCore => !string.IsNullOrWhiteSpace(ReplacementCorePath);
    public bool IsJavaFamily => InstanceType.IsMinecraftJavaRuntimeType();
    public bool SupportsAdvancedEditing => IsJavaFamily;
    public bool IsInstallerBased => InstanceType is InstanceType.MCForge or InstanceType.MCNeoForge or InstanceType.MCCleanroom;
    public string ArgumentsText
    {
        get => string.Join(" ", Arguments);
        set => Arguments = string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    partial void OnArgumentsChanged(string[] value) => OnPropertyChanged(nameof(ArgumentsText));
    partial void OnReplacementCorePathChanged(string value) => OnPropertyChanged(nameof(HasReplacementCore));
    partial void OnInstanceTypeChanged(InstanceType value)
    {
        OnPropertyChanged(nameof(EditableInstanceTypes));
        OnPropertyChanged(nameof(IsJavaFamily));
        OnPropertyChanged(nameof(SupportsAdvancedEditing));
        OnPropertyChanged(nameof(IsInstallerBased));
    }
}
