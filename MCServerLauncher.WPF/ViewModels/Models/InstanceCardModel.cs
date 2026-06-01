using System;
using CommunityToolkit.Mvvm.ComponentModel;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.WPF.Modules;

namespace MCServerLauncher.WPF.ViewModels.Models;

public partial class InstanceCardModel : ObservableObject
{
    [ObservableProperty] private Guid _instanceId;
    [ObservableProperty] private string _instanceName = string.Empty;
    [ObservableProperty] private string _instanceType = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private InstanceStatus _status;
    [ObservableProperty] private double _cpuUsage;
    [ObservableProperty] private long _memoryUsage;
    [ObservableProperty] private bool _isSelected;

    public required Constants.DaemonConfigModel DaemonConfig { get; init; }
}
