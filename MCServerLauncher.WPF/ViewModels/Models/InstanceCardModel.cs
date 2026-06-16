using System;
using System.Windows.Input;
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
    [ObservableProperty] private ulong _memoryTotalBytes;
    [ObservableProperty] private bool _isSelected;

    public required Constants.DaemonConfigModel DaemonConfig { get; init; }
    public required ICommand StartCommand { get; init; }
    public required ICommand StopCommand { get; init; }
    public required ICommand RestartCommand { get; init; }
    public required ICommand KillCommand { get; init; }
    public required ICommand DeleteCommand { get; init; }

    public double CpuUsageProgress => ClampPercentage(CpuUsage);
    public double MemoryUsageProgress => MemoryTotalBytes == 0
        ? 0
        : Math.Clamp(MemoryUsage / (double)MemoryTotalBytes * 100d, 0d, 100d);
    public string StatusText => Status switch
    {
        InstanceStatus.Starting => Lang.Tr["Starting"],
        InstanceStatus.Running => Lang.Tr["Running"],
        InstanceStatus.Stopping => Lang.Tr["Stopping"],
        InstanceStatus.Stopped => Lang.Tr["Stopped"],
        InstanceStatus.Crashed => Lang.Tr["Crashed"],
        _ => Status.ToString()
    };
    public string CpuUsageText => $"{CpuUsageProgress:F2}%";
    public string MemoryUsageText => MemoryTotalBytes == 0
        ? FormatSize(MemoryUsage)
        : $"{MemoryUsageProgress:F2}% ({FormatSize(MemoryUsage)} / {FormatSize(MemoryTotalBytes)})";

    public bool IsActive => Status is InstanceStatus.Starting or InstanceStatus.Running;
    public bool IsBusy => Status is InstanceStatus.Starting or InstanceStatus.Stopping;
    public bool CanStart => Status is InstanceStatus.Stopped or InstanceStatus.Crashed;
    public bool CanStop => IsActive;
    public bool CanRestart => Status == InstanceStatus.Running;
    public bool CanKill => IsActive;
    public bool CanDelete => Status == InstanceStatus.Stopped;

    partial void OnStatusChanged(InstanceStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanRestart));
        OnPropertyChanged(nameof(CanKill));
        OnPropertyChanged(nameof(CanDelete));
    }

    partial void OnCpuUsageChanged(double value)
    {
        var normalizedValue = NormalizeCpu(value);
        if (!value.Equals(normalizedValue))
        {
            CpuUsage = normalizedValue;
            return;
        }

        OnPropertyChanged(nameof(CpuUsageProgress));
        OnPropertyChanged(nameof(CpuUsageText));
    }

    partial void OnMemoryUsageChanged(long value)
    {
        if (value < 0)
        {
            MemoryUsage = 0;
            return;
        }

        OnPropertyChanged(nameof(MemoryUsageProgress));
        OnPropertyChanged(nameof(MemoryUsageText));
    }

    partial void OnMemoryTotalBytesChanged(ulong value)
    {
        OnPropertyChanged(nameof(MemoryUsageProgress));
        OnPropertyChanged(nameof(MemoryUsageText));
    }

    private static double ClampPercentage(double value)
    {
        return NormalizeCpu(value);
    }

    private static double NormalizeCpu(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 100);
    }

    private static string FormatSize(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:F2} {units[unitIndex]}";
    }
}
