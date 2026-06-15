using CommunityToolkit.Mvvm.ComponentModel;
using MCServerLauncher.WPF.Modules;

namespace MCServerLauncher.WPF.ViewModels.Models;

public partial class DaemonCardModel : ObservableObject
{
    [ObservableProperty] private string _friendlyName = string.Empty;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private string _status = "ing";
    [ObservableProperty] private string? _systemType;
    [ObservableProperty] private double _cpuUsage;
    [ObservableProperty] private double _memoryUsage;
    [ObservableProperty] private double _driveUsage;
    [ObservableProperty] private string _cpuUsageText = "--";
    [ObservableProperty] private string _memoryUsageText = "--";
    [ObservableProperty] private string _driveUsageText = "--";
    [ObservableProperty] private string _resourceSummary = "--";
    [ObservableProperty] private string _systemVersion = "--";
    [ObservableProperty] private string _daemonVersion = "--";
    [ObservableProperty] private string _driveUsageTooltip = "--";

    public required Constants.DaemonConfigModel Config { get; set; }
}
