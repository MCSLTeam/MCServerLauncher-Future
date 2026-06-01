using CommunityToolkit.Mvvm.ComponentModel;
using MCServerLauncher.WPF.Modules;

namespace MCServerLauncher.WPF.ViewModels.Models;

public partial class DaemonCardModel : ObservableObject
{
    [ObservableProperty] private string _friendlyName = string.Empty;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private string _status = "ing";
    [ObservableProperty] private string? _systemType;

    public required Constants.DaemonConfigModel Config { get; init; }
}
