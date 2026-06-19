using MCServerLauncher.WPF.Modules;

namespace MCServerLauncher.WPF.Services;

public class NavigationService : INavigationService
{
    public void NavigateTo(string tag, string field, bool navHide = false)
    {
        VisualTreeHelper.Navigate(tag, field, navHide);
    }
}
