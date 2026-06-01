using System;
using Microsoft.Extensions.DependencyInjection;

namespace MCServerLauncher.WPF.ViewModels;

public class ViewModelLocator
{
    private readonly IServiceProvider _serviceProvider;

    public ViewModelLocator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public DaemonManagerViewModel DaemonManager => _serviceProvider.GetRequiredService<DaemonManagerViewModel>();
    public InstanceManagerViewModel InstanceManager => _serviceProvider.GetRequiredService<InstanceManagerViewModel>();
    public SettingsViewModel Settings => _serviceProvider.GetRequiredService<SettingsViewModel>();
    public CreateInstanceViewModel CreateInstance => _serviceProvider.GetRequiredService<CreateInstanceViewModel>();
}
