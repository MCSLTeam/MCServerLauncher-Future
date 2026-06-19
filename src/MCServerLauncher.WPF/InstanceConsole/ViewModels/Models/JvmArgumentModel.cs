using CommunityToolkit.Mvvm.ComponentModel;

namespace MCServerLauncher.WPF.InstanceConsole.ViewModels.Models;

public partial class JvmArgumentModel : ObservableObject
{
    [ObservableProperty] private string _argument = string.Empty;
}
