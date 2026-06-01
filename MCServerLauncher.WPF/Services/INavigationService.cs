namespace MCServerLauncher.WPF.Services;

public interface INavigationService
{
    void NavigateTo(string tag, string field, bool navHide = false);
}
