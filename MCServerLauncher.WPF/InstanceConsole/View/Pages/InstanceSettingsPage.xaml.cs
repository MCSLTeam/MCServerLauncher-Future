using MCServerLauncher.WPF.InstanceConsole.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MCServerLauncher.WPF.InstanceConsole.View.Pages;

public partial class InstanceSettingsPage
{
    private readonly InstanceSettingsViewModel _viewModel;

    public InstanceSettingsPage()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<InstanceSettingsViewModel>();
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }
}
