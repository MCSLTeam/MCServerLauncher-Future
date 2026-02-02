using System;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.Services.Interfaces
{
    /// <summary>
    /// Service for ViewModel-first navigation.
    /// </summary>
    public interface INavigationService
    {
        /// <summary>
        /// Sets the navigation frame to use for navigation.
        /// </summary>
        /// <param name="frame">The frame control to navigate within.</param>
        void SetNavigationFrame(Frame frame);

        /// <summary>
        /// Navigates to the view associated with the specified ViewModel type.
        /// </summary>
        /// <typeparam name="TViewModel">Type of the ViewModel to navigate to.</typeparam>
        void NavigateTo<TViewModel>() where TViewModel : class;

        /// <summary>
        /// Navigates to a view by ViewModel type.
        /// </summary>
        /// <param name="viewModelType">Type of the ViewModel.</param>
        void NavigateTo(Type viewModelType);
    }
}
