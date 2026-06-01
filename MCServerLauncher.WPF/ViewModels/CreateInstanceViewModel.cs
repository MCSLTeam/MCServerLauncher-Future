using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iNKORE.UI.WPF.Controls;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using ListView = System.Windows.Controls.ListView;

namespace MCServerLauncher.WPF.ViewModels;

public partial class CreateInstanceViewModel : ObservableObject
{
    [ObservableProperty] private bool _isDaemonAvailable;

    [RelayCommand]
    private void CheckDaemonAvailability()
    {
        IsDaemonAvailable = DaemonsListManager.Get is { Count: > 0 };
    }

    public async Task<(ContentDialogResult, ListView)> SelectDaemonAsync()
    {
        var daemonDisplayNames = DaemonsListManager.Get!
            .Select(daemon => $"{daemon.FriendlyName} [{(daemon.IsSecure ? "wss" : "ws")}://{daemon.EndPoint}:{daemon.Port}]");

        SimpleStackPanel panel = new();
        ListView listView = new()
        {
            ItemsSource = daemonDisplayNames,
            SelectedIndex = 0,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(listView);

        ScrollViewerEx scroll = new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel
        };

        ContentDialog dialog = new()
        {
            Title = Lang.Tr["PleaseSelectDaemon"],
            PrimaryButtonText = Lang.Tr["Continue"],
            SecondaryButtonText = Lang.Tr["Cancel"],
            DefaultButton = ContentDialogButton.Primary,
            FullSizeDesired = false,
            Content = panel
        };

        var result = await dialog.ShowAsync();
        return (result, listView);
    }
}
