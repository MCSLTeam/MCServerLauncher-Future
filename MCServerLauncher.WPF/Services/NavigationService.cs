using iNKORE.UI.WPF.Modern.Media.Animation;
using MCServerLauncher.WPF.Services.Interfaces;
using MCServerLauncher.WPF.View.Pages;
using MCServerLauncher.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.Services
{
    /// <summary>
    /// Service implementation for ViewModel-first navigation.
    /// </summary>
    public class NavigationService : INavigationService
    {
        private Frame? _navigationFrame;
        private readonly IServiceProvider _serviceProvider;

        private readonly Dictionary<Type, Type> _viewModelToViewMap = new()
        {
            { typeof(HomePageViewModel), typeof(HomePage) },
            { typeof(SettingsPageViewModel), typeof(SettingsPage) },
            { typeof(CreateInstancePageViewModel), typeof(CreateInstancePage) },
            { typeof(DaemonManagerPageViewModel), typeof(DaemonManagerPage) },
            { typeof(InstanceManagerPageViewModel), typeof(InstanceManagerPage) },
            { typeof(ResDownloadPageViewModel), typeof(ResDownloadPage) },
            { typeof(HelpPageViewModel), typeof(HelpPage) }
        };

        public NavigationService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void SetNavigationFrame(Frame frame)
        {
            _navigationFrame = frame;
        }

        public void NavigateTo<TViewModel>() where TViewModel : class
        {
            NavigateTo(typeof(TViewModel));
        }

        public void NavigateTo(Type viewModelType)
        {
            if (_navigationFrame == null)
            {
                throw new InvalidOperationException("Navigation frame has not been set. Call SetNavigationFrame first.");
            }

            if (!_viewModelToViewMap.TryGetValue(viewModelType, out var viewType))
            {
                throw new ArgumentException($"No view mapped for ViewModel type: {viewModelType.Name}");
            }

            var view = _serviceProvider.GetRequiredService(viewType) as Page;
            if (view == null)
            {
                throw new InvalidOperationException($"Could not resolve view of type: {viewType.Name}");
            }

            _navigationFrame.Navigate(view, new DrillInNavigationTransitionInfo());
        }
    }
}
