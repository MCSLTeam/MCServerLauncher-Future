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
    [ObservableProperty] private string _cpuUsageText = Lang.Tr["Status_NotLoaded"];
    [ObservableProperty] private string _memoryUsageText = Lang.Tr["Status_NotLoaded"];
    [ObservableProperty] private string _driveUsageText = Lang.Tr["Status_NotLoaded"];
    [ObservableProperty] private string _resourceSummary = Lang.Tr["Status_NotLoaded"];
    [ObservableProperty] private string _systemVersion = Lang.Tr["Status_NotLoaded"];
    [ObservableProperty] private string _daemonVersion = Lang.Tr["Status_NotLoaded"];
    [ObservableProperty] private string _driveUsageTooltip = Lang.Tr["Status_NotLoaded"];

    public required Constants.DaemonConfigModel Config { get; set; }

    public void MarkResourceLoadFailed()
    {
        CpuUsage = 0;
        MemoryUsage = 0;
        DriveUsage = 0;
        CpuUsageText = Lang.Tr["Status_LoadFailed"];
        MemoryUsageText = Lang.Tr["Status_LoadFailed"];
        DriveUsageText = Lang.Tr["Status_LoadFailed"];
        ResourceSummary = Lang.Tr["Status_LoadFailed"];
        SystemVersion = Lang.Tr["Status_LoadFailed"];
        DaemonVersion = Lang.Tr["Status_LoadFailed"];
        DriveUsageTooltip = Lang.Tr["Status_LoadFailed"];
    }
}
