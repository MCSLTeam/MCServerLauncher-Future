using MCServerLauncher.Common.Contracts.Audit;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.Features.Plugins;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.View.Pages;

public partial class PluginExtensionCenterPage
{
    private readonly PluginExtensionCenterService _service = new();
    private readonly Dictionary<string, PluginExtensionStateSnapshot> _stateSnapshots = new(StringComparer.Ordinal);
    private readonly List<IAsyncDisposable> _eventSubscriptions = [];
    private readonly HashSet<string> _subscribedEventNames = new(StringComparer.Ordinal);
    private DaemonClient.DaemonClient? _subscribedDaemon;
    private PluginExtensionCenterEntryViewModel? _detailEntry;
    private int _targetFilterIndex;

    public PluginExtensionCenterPage()
    {
        InitializeComponent();
        DataContext = this;
        SearchTextBox.TextChanged += (_, _) => ApplySearchFilter();
        TargetFilterCombo.SelectionChanged += TargetFilterCombo_SelectionChanged;
        _targetFilterIndex = TargetFilterCombo.SelectedIndex;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                RestoreEntries();
            else
                _ = ClearDaemonEventSubscriptionsAsync();
        };
    }

    public ObservableCollection<PluginExtensionCenterEntryViewModel> Entries { get; } = [];

    public ObservableCollection<PluginExtensionCenterEntryViewModel> FilteredEntries { get; } = [];

    private void ApplySearchFilter()
    {
        var query = SearchTextBox.Text.Trim();
        RefreshFilteredEntries(query);
    }

    private void TargetFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TargetFilterCombo.SelectedIndex < 0)
            return;
        _targetFilterIndex = TargetFilterCombo.SelectedIndex;
        if (SearchTextBox is null)
            return;
        RefreshFilteredEntries(SearchTextBox.Text.Trim());
    }

    private void RefreshFilteredEntries(string query = "")
    {
        FilteredEntries.Clear();
        IEnumerable<PluginExtensionCenterEntryViewModel> source = Entries;

        // 目标过滤：0=全部 1=本机 2=服务器
        if (_targetFilterIndex == 1)
        {
            source = source.Where(vm => vm.ScopeKind is PluginExtensionScopeKind.ClientOnly or PluginExtensionScopeKind.Both);
        }
        else if (_targetFilterIndex == 2)
        {
            source = source.Where(vm => vm.ScopeKind is PluginExtensionScopeKind.ServerOnly or PluginExtensionScopeKind.Both);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(vm =>
                vm.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                vm.MetaSummary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                vm.Summary.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var vm in source)
            FilteredEntries.Add(vm);
        ExtensionGrid.SelectedItems.Clear();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RestoreEntries();

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "MCSL Extension Package (*.mpx)|*.mpx|Zip Package (*.zip)|*.zip|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
            return;

        var reviewResult = _service.ReviewPackage(dialog.FileName);
        if (!reviewResult.Ok || reviewResult.Review is null)
        {
            StatusText.Text = reviewResult.Diagnostics.Count > 0
                ? string.Join(Environment.NewLine, reviewResult.Diagnostics.Take(6).Select(diagnostic => $"{diagnostic.Path}: {diagnostic.Message}"))
                : $"{reviewResult.Code}: {reviewResult.Message}";
            return;
        }

        if (!ConfirmInstall(reviewResult.Review, dialog.SafeFileName))
        {
            StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_InstallCancelled"];
            return;
        }

        var result = _service.InstallReviewedPackage(reviewResult.Review);
        if (!result.Ok)
        {
            StatusText.Text = result.Diagnostics.Count > 0
                ? string.Join(Environment.NewLine, result.Diagnostics.Take(6).Select(diagnostic => $"{diagnostic.Path}: {diagnostic.Message}"))
                : $"{result.Code}: {result.Message}";
            return;
        }

        RestoreEntries(result.Entry?.Id);
        StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_InstallSuccess"];
    }

    private PluginExtensionCenterEntryViewModel? CurrentDetail => _detailEntry;

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentDetail is not { } selected)
            return;

        var removed = _service.Uninstall(selected.Entry.Id);
        RestoreEntries();
        StatusText.Text = removed
            ? Lang.Tr["Plugin_ExtensionCenter_RemoveSuccess"]
            : Lang.Tr["Plugin_ExtensionCenter_RemoveMissing"];
        ShowList();
    }

    public ObservableCollection<PluginMarketplaceItemViewModel> MarketplaceItems { get; } = [];

    private PluginRegistryClient? _marketplaceClient;

    private static string MarketplaceRegistryUrl =>
        SettingsManager.Get?.App?.PluginRegistryUrl ?? "http://127.0.0.1:8741";

    public ObservableCollection<PluginMarketplaceUpdateViewModel> MarketplaceUpdateItems { get; } = [];

    private async void MarketplaceCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        var url = MarketplaceUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MarketplaceStatus.Text = Lang.Tr["Plugin_ExtensionCenter_MarketplaceError"];
            return;
        }

        MarketplaceUpdateItems.Clear();
        MarketplaceList.Visibility = Visibility.Collapsed;
        UpdatesList.Visibility = Visibility.Visible;
        MarketplaceStatus.Text = string.Empty;

        var entries = _service.List();
        if (entries.Count == 0)
        {
            MarketplaceStatus.Text = Lang.Tr["Plugin_ExtensionCenter_MarketplaceNoInstalled"];
            return;
        }

        try
        {
            var client = new PluginRegistryClient(url);
            _marketplaceClient = client;
            foreach (var entry in entries)
            {
                var info = await client.GetUpdatesAsync(entry.Id, entry.Version).ConfigureAwait(true);
                if (info?.UpdateAvailable == true && !string.IsNullOrWhiteSpace(info.LatestVersion))
                {
                    MarketplaceUpdateItems.Add(new PluginMarketplaceUpdateViewModel(
                        entry.Id,
                        entry.Version,
                        info.LatestVersion,
                        info.Changelog));
                }
            }

            if (MarketplaceUpdateItems.Count == 0)
                MarketplaceStatus.Text = Lang.Tr["Plugin_ExtensionCenter_MarketplaceNoUpdates"];
        }
        catch (Exception exception) when (exception is PluginRegistryException or System.Net.Http.HttpRequestException or System.Threading.Tasks.TaskCanceledException)
        {
            MarketplaceStatus.Text = Lang.Tr["Plugin_ExtensionCenter_MarketplaceError"] + " " + exception.Message;
            MarketplaceList.Visibility = Visibility.Visible;
            UpdatesList.Visibility = Visibility.Collapsed;
        }
    }

    private async void MarketplaceUpdate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PluginMarketplaceUpdateViewModel update)
            return;
        await InstallMarketplaceUpdateAsync(update);
    }

    private async Task InstallMarketplaceUpdateAsync(PluginMarketplaceUpdateViewModel update)
    {
        var client = _marketplaceClient;
        if (client is null)
            return;

        MarketplaceStatus.Text = string.Empty;
        try
        {
            var bytes = await client.DownloadVersionAsync(update.Id, update.LatestVersion).ConfigureAwait(true);
            var reviewResult = _service.ReviewPackageBytes(bytes, $"{update.Id}-{update.LatestVersion}.mpx");
            if (!reviewResult.Ok || reviewResult.Review is null)
            {
                MarketplaceStatus.Text = reviewResult.Diagnostics.Count > 0
                    ? string.Join(Environment.NewLine, reviewResult.Diagnostics.Take(6).Select(diagnostic => $"{diagnostic.Path}: {diagnostic.Message}"))
                    : $"{reviewResult.Code}: {reviewResult.Message}";
                return;
            }

            if (!ConfirmInstall(reviewResult.Review, $"{update.Id}-{update.LatestVersion}.mpx"))
            {
                MarketplaceStatus.Text = Lang.Tr["Plugin_ExtensionCenter_InstallCancelled"];
                return;
            }

            var result = _service.InstallReviewedPackage(reviewResult.Review);
            if (!result.Ok)
            {
                MarketplaceStatus.Text = result.Diagnostics.Count > 0
                    ? string.Join(Environment.NewLine, result.Diagnostics.Take(6).Select(diagnostic => $"{diagnostic.Path}: {diagnostic.Message}"))
                    : $"{result.Code}: {result.Message}";
                return;
            }

            MarketplaceUpdateItems.Remove(update);
            RestoreEntries(result.Entry?.Id);
            StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_MarketplaceInstalled"];
        }
        catch (Exception exception) when (exception is PluginRegistryException or System.Net.Http.HttpRequestException or System.Threading.Tasks.TaskCanceledException)
        {
            MarketplaceStatus.Text = Lang.Tr["Plugin_ExtensionCenter_MarketplaceError"] + " " + exception.Message;
        }
    }

    private void MarketplaceToggle_Click(object sender, RoutedEventArgs e)
    {
        if (MarketplaceToggle.IsChecked == true)
            ShowMarketplace();
        else
        {
            MarketplaceRoot.Visibility = Visibility.Collapsed;
            if (Entries.Count == 0)
                ShowEmpty();
            else
                ShowList();
        }
    }

    private void ShowMarketplace()
    {
        _detailEntry = null;
        ExtensionGrid.SelectedItems.Clear();
        DetailView.Visibility = Visibility.Collapsed;
        BatchOperationBar.Visibility = Visibility.Collapsed;
        ListViewRoot.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        TrustRootsRoot.Visibility = Visibility.Collapsed;
        MarketplaceRoot.Visibility = Visibility.Visible;

        MarketplaceUrlBox.Text = MarketplaceRegistryUrl;
        if (MarketplaceItems.Count == 0)
            MarketplaceSearch_Click(this, new RoutedEventArgs());
    }

    private async void MarketplaceSearch_Click(object sender, RoutedEventArgs e)
    {
        var url = MarketplaceUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
            return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            MarketplaceStatus.Text = Lang.Tr["Plugin_ExtensionCenter_MarketplaceError"] + " " + url;
            return;
        }

        SettingsManager.SaveSetting("App.PluginRegistryUrl", url);
        var query = MarketplaceSearchBox.Text.Trim();
        MarketplaceStatus.Text = string.Empty;
        MarketplaceItems.Clear();

        try
        {
            var client = new PluginRegistryClient(url);
            _marketplaceClient = client;
            var plugins = await client.ListPluginsAsync(query).ConfigureAwait(true);
            foreach (var plugin in plugins)
                MarketplaceItems.Add(new PluginMarketplaceItemViewModel(plugin));

            if (MarketplaceItems.Count == 0)
                MarketplaceStatus.Text = Lang.Tr["Plugin_ExtensionCenter_MarketplaceEmpty"];
        }
        catch (Exception exception) when (exception is PluginRegistryException or System.Net.Http.HttpRequestException or System.Threading.Tasks.TaskCanceledException)
        {
            MarketplaceStatus.Text = Lang.Tr["Plugin_ExtensionCenter_MarketplaceError"] + " " + exception.Message;
        }
    }

    private void MarketplaceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MarketplaceStatus.Text = string.Empty;
    }

    private async void MarketplaceInstall_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PluginMarketplaceItemViewModel item)
            return;
        await InstallMarketplaceItemAsync(item, item.LatestVersion);
    }

    /// <summary>
    /// Handles a `mcsl://install/&lt;id&gt;[/&lt;version&gt;]` deep link: opens the
    /// marketplace, finds the package and starts the install review flow.
    /// </summary>
    public async void HandleDeepLink(Features.Plugins.McslDeepLinkPayload payload)
    {
        if (!string.Equals(payload.Kind, "install", StringComparison.OrdinalIgnoreCase))
            return;
        if (!MarketplaceToggle.IsChecked.HasValue || MarketplaceToggle.IsChecked.Value != true)
            MarketplaceToggle.IsChecked = true;
        ShowMarketplace();
        MarketplaceSearchBox.Text = payload.PackageId;
        MarketplaceSearch_Click(this, new RoutedEventArgs());
        // Wait for the search results, then pick the matching package.
        for (var attempt = 0; attempt < 40; attempt += 1)
        {
            await Task.Delay(100);
            var match = MarketplaceItems.FirstOrDefault(item =>
                string.Equals(item.Id, payload.PackageId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                await InstallMarketplaceItemAsync(match, payload.Version);
                return;
            }
        }
        MarketplaceStatus.Text = string.Format(
            Lang.Tr["Plugin_ExtensionCenter_MarketplaceError"] + " {0}",
            payload.PackageId);
    }

    private async Task InstallMarketplaceItemAsync(PluginMarketplaceItemViewModel item, string? version)
    {
        var client = _marketplaceClient;
        if (client is null)
            return;

        version ??= item.LatestVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            MarketplaceStatus.Text = Lang.Tr["Plugin_ExtensionCenter_MarketplaceError"];
            return;
        }

        MarketplaceStatus.Text = string.Empty;
        try
        {
            var bytes = await client.DownloadVersionAsync(item.Id, version).ConfigureAwait(true);
            var reviewResult = _service.ReviewPackageBytes(bytes, $"{item.Id}-{version}.mpx");
            if (!reviewResult.Ok || reviewResult.Review is null)
            {
                MarketplaceStatus.Text = reviewResult.Diagnostics.Count > 0
                    ? string.Join(Environment.NewLine, reviewResult.Diagnostics.Take(6).Select(diagnostic => $"{diagnostic.Path}: {diagnostic.Message}"))
                    : $"{reviewResult.Code}: {reviewResult.Message}";
                return;
            }

            // Resolve transitive dependencies first (auto-installed after
            // offline validation, mirroring the Web client).
            var visited = new HashSet<string>(StringComparer.Ordinal) { item.Id };
            var dependencyInstall = await InstallDependenciesAsync(client, reviewResult.Review.Package.Dependencies, visited).ConfigureAwait(true);
            if (!dependencyInstall)
                return;

            if (!ConfirmInstall(reviewResult.Review, $"{item.Id}-{version}.mpx"))
            {
                MarketplaceStatus.Text = Lang.Tr["Plugin_ExtensionCenter_InstallCancelled"];
                return;
            }

            var result = _service.InstallReviewedPackage(reviewResult.Review);
            if (!result.Ok)
            {
                MarketplaceStatus.Text = result.Diagnostics.Count > 0
                    ? string.Join(Environment.NewLine, result.Diagnostics.Take(6).Select(diagnostic => $"{diagnostic.Path}: {diagnostic.Message}"))
                    : $"{result.Code}: {result.Message}";
                return;
            }

            RestoreEntries(result.Entry?.Id);
            MarketplaceToggle.IsChecked = false;
            MarketplaceRoot.Visibility = Visibility.Collapsed;
            ShowList();
            StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_MarketplaceInstalled"];
        }
        catch (Exception exception) when (exception is PluginRegistryException or System.Net.Http.HttpRequestException or System.Threading.Tasks.TaskCanceledException)
        {
            MarketplaceStatus.Text = Lang.Tr["Plugin_ExtensionCenter_MarketplaceError"] + " " + exception.Message;
        }
    }

    /// <summary>
    /// Recursively download + validate + install declared dependencies
    /// (dependencies-first). Already-installed packages are skipped;
    /// version conflicts and cycles are reported instead of looping.
    /// </summary>
    private async Task<bool> InstallDependenciesAsync(
        PluginRegistryClient client,
        IReadOnlyList<PluginMpxExtensionDependency> dependencies,
        HashSet<string> visited)
    {
        foreach (var dependency in dependencies)
        {
            if (!visited.Add(dependency.Id))
                continue; // cycle or duplicate edge

            var installed = _service.List();
            if (installed.Any(entry => StringComparer.Ordinal.Equals(entry.Id, dependency.Id)))
                continue;

            byte[] bytes;
            try
            {
                bytes = await client.DownloadVersionAsync(dependency.Id, dependency.Version).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is PluginRegistryException or System.Net.Http.HttpRequestException or System.Threading.Tasks.TaskCanceledException)
            {
                MarketplaceStatus.Text = Lang.Tr["Plugin_ExtensionCenter_MarketplaceError"] + " " + exception.Message;
                return false;
            }

            var reviewResult = _service.ReviewPackageBytes(bytes, $"{dependency.Id}-{dependency.Version}.mpx");
            if (!reviewResult.Ok || reviewResult.Review is null)
            {
                MarketplaceStatus.Text = string.Format(
                    Lang.Tr["Plugin_ExtensionCenter_MarketplaceDependencyFailed"],
                    dependency.Id,
                    reviewResult.Diagnostics.Count > 0
                        ? string.Join(Environment.NewLine, reviewResult.Diagnostics.Take(4).Select(diagnostic => $"{diagnostic.Path}: {diagnostic.Message}"))
                        : $"{reviewResult.Code}: {reviewResult.Message}");
                return false;
            }

            if (!await InstallDependenciesAsync(client, reviewResult.Review.Package.Dependencies, visited).ConfigureAwait(true))
                return false;

            var result = _service.InstallReviewedPackage(reviewResult.Review);
            if (!result.Ok)
            {
                MarketplaceStatus.Text = string.Format(
                    Lang.Tr["Plugin_ExtensionCenter_MarketplaceDependencyFailed"],
                    dependency.Id,
                    $"{result.Code}: {result.Message}");
                return false;
            }
        }
        return true;
    }

    private void TrustRootsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (TrustRootsToggle.IsChecked == true)
        {
            ShowTrustRoots();
        }
        else
        {
            TrustRootsRoot.Visibility = Visibility.Collapsed;
            if (MarketplaceToggle.IsChecked == true)
                ShowMarketplace();
            else if (Entries.Count == 0)
                ShowEmpty();
            else
                ShowList();
        }
    }

    private void ShowTrustRoots()
    {
        _detailEntry = null;
        ExtensionGrid.SelectedItems.Clear();
        DetailView.Visibility = Visibility.Collapsed;
        BatchOperationBar.Visibility = Visibility.Collapsed;
        ListViewRoot.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        MarketplaceRoot.Visibility = Visibility.Collapsed;
        TrustRootsRoot.Visibility = Visibility.Visible;
        RefreshTrustRoots();
    }

    private void RefreshTrustRoots()
    {
        TrustRootsList.ItemsSource = null;
        TrustRootsList.ItemsSource = _service.TrustRoots.List()
            .Select(root => new PluginTrustRootViewModel(root))
            .ToArray();
        TrustRootStatus.Text = string.Empty;
    }

    private void TrustRootAdd_Click(object sender, RoutedEventArgs e)
    {
        var publisher = TrustRootPublisherBox.Text.Trim();
        var keyId = TrustRootKeyIdBox.Text.Trim();
        var spkiBase64 = TrustRootSpkiBox.Text.Trim().Replace("\n", "").Replace(" ", "");
        byte[] spki;
        try
        {
            spki = Convert.FromBase64String(spkiBase64);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentNullException)
        {
            TrustRootStatus.Text = Lang.Tr["Plugin_ExtensionCenter_TrustRootInvalid"];
            return;
        }

        _service.TrustRoots.Add(publisher, keyId, spki);
        TrustRootPublisherBox.Clear();
        TrustRootKeyIdBox.Clear();
        TrustRootSpkiBox.Clear();
        TrustRootStatus.Text = Lang.Tr["Plugin_ExtensionCenter_TrustRootAdded"];
        RefreshTrustRoots();
    }

    private void TrustRootRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PluginTrustRootViewModel viewModel)
            return;
        _service.TrustRoots.Remove(viewModel.Publisher, viewModel.Fingerprint);
        TrustRootStatus.Text = Lang.Tr["Plugin_ExtensionCenter_TrustRootRemoved"];
        RefreshTrustRoots();
    }

    private async void DeployDaemon_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentDetail is not { } selected)
            return;

        await DeployDaemonBundleAsync(selected.Entry).ConfigureAwait(false);
    }

    private async void EnableDaemon_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentDetail is not { } selected)
            return;

        await SetDaemonBundleEnabledAsync(selected.Entry, enabled: true).ConfigureAwait(false);
    }

    private async void DisableDaemon_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentDetail is not { } selected)
            return;

        await SetDaemonBundleEnabledAsync(selected.Entry, enabled: false).ConfigureAwait(false);
    }

    private async void RequestDaemonRestart_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentDetail is not { } selected)
            return;

        await RequestDaemonShutdownAsync(selected.Entry).ConfigureAwait(false);
    }

    private async void RemoveDaemon_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentDetail is not { } selected)
            return;

        await RemoveDaemonBundleAsync(selected.Entry).ConfigureAwait(false);
    }

    private void ExtensionGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = ExtensionGrid.SelectedItems.Cast<PluginExtensionCenterEntryViewModel>().ToArray();
        if (selected.Length == 1)
        {
            ShowDetail(selected[0]);
        }
        else if (selected.Length > 1)
        {
            ShowBatchBar(selected);
        }
        else
        {
            ShowList();
        }
    }

    private void BackToList_Click(object sender, RoutedEventArgs e)
    {
        ExtensionGrid.SelectedItems.Clear();
        ShowList();
    }

    private void RestoreEntries(string? selectedId = null)
    {
        var entries = _service.Restore();
        Entries.Clear();
        foreach (var entry in entries)
            Entries.Add(new PluginExtensionCenterEntryViewModel(entry));

        PruneStateSnapshots();
        _ = RefreshDaemonEventSubscriptionsAsync();
        RefreshFilteredEntries(SearchTextBox.Text.Trim());

        if (Entries.Count == 0)
        {
            ShowEmpty();
            StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_Empty"];
            return;
        }

        var target = _detailEntry is not null && Entries.Any(vm => vm.Id == _detailEntry.Id)
            ? Entries.First(vm => vm.Id == _detailEntry.Id)
            : Entries.FirstOrDefault(vm => vm.Id == selectedId);
        if (target is not null)
            ShowDetail(target);
        else
            ShowList();
    }

    private void ShowDetail(PluginExtensionCenterEntryViewModel vm)
    {
        _detailEntry = vm;
        ExtensionGrid.SelectedItems.Clear();
        ExtensionGrid.SelectedItems.Add(vm);
        ListViewRoot.Visibility = Visibility.Collapsed;
        DetailView.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        BatchOperationBar.Visibility = Visibility.Collapsed;
        MarketplaceRoot.Visibility = Visibility.Collapsed;
        TrustRootsRoot.Visibility = Visibility.Collapsed;
        UpdateDetailMenuEnabled(vm);
        _ = RenderSelectedAsync(vm.Entry);
    }

    private void ShowList()
    {
        _detailEntry = null;
        ExtensionGrid.SelectedItems.Clear();
        DetailView.Visibility = Visibility.Collapsed;
        BatchOperationBar.Visibility = Visibility.Collapsed;
        MarketplaceRoot.Visibility = Visibility.Collapsed;
        TrustRootsRoot.Visibility = Visibility.Collapsed;
        if (FilteredEntries.Count == 0)
            ShowEmpty();
        else
        {
            ListViewRoot.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowEmpty()
    {
        _detailEntry = null;
        ExtensionGrid.SelectedItems.Clear();
        DetailView.Visibility = Visibility.Collapsed;
        BatchOperationBar.Visibility = Visibility.Collapsed;
        ListViewRoot.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
        MarketplaceRoot.Visibility = Visibility.Collapsed;
        TrustRootsRoot.Visibility = Visibility.Collapsed;
    }

    private void ShowBatchBar(IReadOnlyList<PluginExtensionCenterEntryViewModel> selected)
    {
        _detailEntry = null;
        DetailView.Visibility = Visibility.Collapsed;
        ListViewRoot.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        SelectedCountTextBlock.Text = string.Format(Lang.Tr["SelectedCount"], selected.Count);
        BatchOperationBar.Visibility = Visibility.Visible;
    }

    private void UpdateDetailMenuEnabled(PluginExtensionCenterEntryViewModel vm)
    {
        RemoveClientMenuItem.IsEnabled = true;
        var hasDaemon = vm.Entry.DeploymentPlan.Daemon?.Plugin is not null;
        DeployDaemonMenuItem.IsEnabled = hasDaemon;
        EnableDaemonMenuItem.IsEnabled = hasDaemon;
        DisableDaemonMenuItem.IsEnabled = hasDaemon;
        RequestDaemonRestartMenuItem.IsEnabled = hasDaemon;
        RemoveDaemonMenuItem.IsEnabled = hasDaemon;
    }

    private void SelectAllBatch_Click(object sender, RoutedEventArgs e)
    {
        ExtensionGrid.SelectedItems.Clear();
        foreach (var vm in FilteredEntries)
            ExtensionGrid.SelectedItems.Add(vm);
        ShowBatchBar(FilteredEntries.ToArray());
    }

    private void CancelSelectionBatch_Click(object sender, RoutedEventArgs e)
    {
        ExtensionGrid.SelectedItems.Clear();
        ShowList();
    }

    private async void BatchInstallDaemon_Click(object sender, RoutedEventArgs e)
    {
        var selected = ExtensionGrid.SelectedItems.Cast<PluginExtensionCenterEntryViewModel>()
            .Where(vm => vm.Entry.DeploymentPlan.Daemon?.Plugin is not null)
            .ToArray();
        if (selected.Length == 0)
            return;

        var success = 0;
        var fail = 0;
        foreach (var vm in selected)
        {
            try
            {
                await DeployDaemonBundleAsync(vm.Entry).ConfigureAwait(false);
                success++;
            }
            catch
            {
                fail++;
            }
        }

        StatusText.Text = string.Format(Lang.Tr["BatchOperationResult"], success, fail);
        ExtensionGrid.SelectedItems.Clear();
        ShowList();
    }

    private void BatchRemove_Click(object sender, RoutedEventArgs e)
    {
        var selected = ExtensionGrid.SelectedItems.Cast<PluginExtensionCenterEntryViewModel>().ToArray();
        var success = 0;
        var fail = 0;
        foreach (var vm in selected)
        {
            if (_service.Uninstall(vm.Entry.Id))
                success++;
            else
                fail++;
        }

        RestoreEntries();
        StatusText.Text = string.Format(Lang.Tr["BatchOperationResult"], success, fail);
        ShowList();
    }

    private async void CardDeployDaemon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: PluginExtensionCenterEntryViewModel vm })
            await DeployDaemonBundleAsync(vm.Entry).ConfigureAwait(false);
    }

    private async void CardEnableDaemon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: PluginExtensionCenterEntryViewModel vm })
            await SetDaemonBundleEnabledAsync(vm.Entry, enabled: true).ConfigureAwait(false);
    }

    private async void CardDisableDaemon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: PluginExtensionCenterEntryViewModel vm })
            await SetDaemonBundleEnabledAsync(vm.Entry, enabled: false).ConfigureAwait(false);
    }

    private async void CardRequestDaemonRestart_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: PluginExtensionCenterEntryViewModel vm })
            await RequestDaemonShutdownAsync(vm.Entry).ConfigureAwait(false);
    }

    private async void CardRemoveDaemon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: PluginExtensionCenterEntryViewModel vm })
            await RemoveDaemonBundleAsync(vm.Entry).ConfigureAwait(false);
    }

    private void CardRemoveClient_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: PluginExtensionCenterEntryViewModel vm })
            return;
        if (_service.Uninstall(vm.Entry.Id))
            StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_RemoveSuccess"];
        else
            StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_RemoveMissing"];
        RestoreEntries();
    }

    private async Task RenderSelectedAsync(PluginClientExtensionCacheEntry entry)
    {
        RemoveClientMenuItem.IsEnabled = true;
        DeployDaemonMenuItem.IsEnabled = entry.DeploymentPlan.Daemon?.Plugin is not null;
        EnableDaemonMenuItem.IsEnabled = entry.DeploymentPlan.Daemon?.Plugin is not null;
        DisableDaemonMenuItem.IsEnabled = entry.DeploymentPlan.Daemon?.Plugin is not null;
        RequestDaemonRestartMenuItem.IsEnabled = entry.DeploymentPlan.Daemon?.Plugin is not null;
        RemoveDaemonMenuItem.IsEnabled = entry.DeploymentPlan.Daemon?.Plugin is not null;
        var summary = PluginExtensionCenterService.Summarize(entry);
        SelectedTitle.Text = entry.Id;
        var scopeKind = _detailEntry is not null
            ? _detailEntry
            : new PluginExtensionCenterEntryViewModel(entry);
        SelectedSubtitle.Text = string.Format(
            Lang.Tr["Plugin_ExtensionCenter_SelectedSummary"],
            entry.Version,
            summary.Commands.Count,
            summary.Resources.Count,
            summary.HostCapabilities.Count)
            + Environment.NewLine
            + $"{scopeKind.ScopeKindText} · {scopeKind.ScopeKindDescription}";

        if (entry.UiSchema is null)
        {
            RenderHost.Content = new TextBlock
            {
                Text = Lang.Tr["Plugin_ExtensionCenter_NoUi"],
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75
            };
            StatusText.Text = await FormatMetadataAsync(entry, summary);
            return;
        }

        try
        {
            var runtimeHost = CreateRuntimeHost();
            var result = await runtimeHost.RenderAsync(
                new PluginClientExtensionRenderRequest(
                    entry,
                    GetState(entry),
                    uiEvent => DispatchUiEventAsync(runtimeHost, entry, uiEvent)),
                Dispatcher);
            if (result.Ok && result.Root is not null)
            {
                RenderHost.Content = result.Root;
                StatusText.Text = result.ScriptResult is { Success: false }
                    ? result.ScriptResult.Message
                    : await FormatMetadataAsync(entry, summary);
            }
            else
            {
                RenderHost.Content = null;
                StatusText.Text = string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message));
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            RenderHost.Content = null;
            StatusText.Text = exception.Message;
        }
    }

    private PluginClientExtensionRuntimeHost CreateRuntimeHost()
    {
        var daemon = GetReadyDaemon();
        return daemon is null
            ? new PluginClientExtensionRuntimeHost()
            : new PluginClientExtensionRuntimeHost(
                daemonQueryBridge: PluginUiDaemonQueryBridge.FromDaemonClient(daemon),
                commandDispatcher: PluginExtensionCommandDispatcher.FromDaemonClient(daemon));
    }

    private static DaemonClient.DaemonClient? GetReadyDaemon() =>
        DaemonsWsManager.DaemonConnections.Values.FirstOrDefault(client => client.ConnectionState == DaemonConnectionState.Ready);

    private IReadOnlyDictionary<string, object?> GetState(PluginClientExtensionCacheEntry entry)
    {
        if (_stateSnapshots.TryGetValue(entry.Id, out var snapshot))
            return snapshot.State;
        return new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    private async Task RefreshDaemonEventSubscriptionsAsync()
    {
        var daemon = GetReadyDaemon();
        if (daemon is null)
        {
            await ClearDaemonEventSubscriptionsAsync().ConfigureAwait(false);
            return;
        }

        var eventNames = Entries
            .SelectMany(entry => entry.Entry.EventPermissions)
            .Select(PluginExtensionDaemonEventBridge.ToDaemonEventName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (eventNames.Length == 0)
        {
            await ClearDaemonEventSubscriptionsAsync().ConfigureAwait(false);
            _subscribedDaemon = daemon;
            return;
        }

        if (ReferenceEquals(_subscribedDaemon, daemon) && _subscribedEventNames.SetEquals(eventNames!))
            return;

        await ClearDaemonEventSubscriptionsAsync().ConfigureAwait(false);
        _subscribedDaemon = daemon;

        foreach (var eventName in eventNames)
        {
            var subscription = await SubscribeDaemonEventAsync(daemon, eventName!).ConfigureAwait(false);
            if (subscription is not null)
            {
                _eventSubscriptions.Add(subscription);
                _subscribedEventNames.Add(eventName!);
            }
        }
    }

    private void PruneStateSnapshots()
    {
        var activeIds = Entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _stateSnapshots.Keys.ToArray())
        {
            if (!activeIds.Contains(key))
                _stateSnapshots.Remove(key);
        }
    }

    private async Task ClearDaemonEventSubscriptionsAsync()
    {
        var subscriptions = _eventSubscriptions.ToArray();
        _eventSubscriptions.Clear();
        _subscribedEventNames.Clear();
        _subscribedDaemon = null;
        foreach (var subscription in subscriptions)
            await subscription.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<IAsyncDisposable?> SubscribeDaemonEventAsync(DaemonClient.DaemonClient daemon, string eventName)
    {
        if (eventName == BuiltInProtocolDefinitions.InstanceCatalogChanged.Name.Value)
        {
            var result = await daemon.SubscribeAsync(
                BuiltInProtocolDefinitions.InstanceCatalogChanged,
                DaemonEventFilter<EmptyRequest>.Wildcard,
                @event => OnBuiltInDaemonEventAsync(eventName, @event)).ConfigureAwait(false);
            return result.IsOk(out var subscription) ? subscription : null;
        }
        if (eventName == BuiltInProtocolDefinitions.InstanceLog.Name.Value)
        {
            var result = await daemon.SubscribeAsync(
                BuiltInProtocolDefinitions.InstanceLog,
                DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
                @event => OnBuiltInDaemonEventAsync(eventName, @event)).ConfigureAwait(false);
            return result.IsOk(out var subscription) ? subscription : null;
        }
        if (eventName == BuiltInProtocolDefinitions.DaemonReport.Name.Value)
        {
            var result = await daemon.SubscribeAsync(
                BuiltInProtocolDefinitions.DaemonReport,
                DaemonEventFilter<EmptyRequest>.Wildcard,
                @event => OnBuiltInDaemonEventAsync(eventName, @event)).ConfigureAwait(false);
            return result.IsOk(out var subscription) ? subscription : null;
        }
        if (eventName == BuiltInProtocolDefinitions.Notification.Name.Value)
        {
            var result = await daemon.SubscribeAsync(
                BuiltInProtocolDefinitions.Notification,
                DaemonEventFilter<NotificationEventMeta>.Wildcard,
                @event => OnBuiltInDaemonEventAsync(eventName, @event)).ConfigureAwait(false);
            return result.IsOk(out var subscription) ? subscription : null;
        }
        if (PluginExtensionDaemonEventBridge.IsPluginExtensionEventName(eventName))
        {
            var descriptor = PluginExtensionDaemonEventBridge.CreatePluginExtensionEventDescriptor(eventName);
            var result = await daemon.SubscribeAsync(
                descriptor,
                DaemonEventFilter<EmptyRequest>.Wildcard,
                @event => OnPluginExtensionEventAsync(eventName, @event)).ConfigureAwait(false);
            return result.IsOk(out var subscription) ? subscription : null;
        }

        return null;
    }

    private async Task OnPluginExtensionEventAsync(
        string sourceEventName,
        DaemonEvent<ExtensionProtocolDispatchResult, EmptyRequest> daemonEvent)
    {
        var parsed = PluginExtensionDaemonEventBridge.ParsePluginExtensionEvent(daemonEvent);
        if (!parsed.Ok)
            return;

        var pluginId = GetPluginId(parsed);
        if (pluginId is null || !StringComparer.Ordinal.Equals(sourceEventName, $"plugin.{pluginId}.event.extension"))
            return;

        var entry = await Dispatcher.InvokeAsync(() => Entries.FirstOrDefault(item => item.Id == pluginId)?.Entry);
        if (entry is null || !entry.EventPermissions.Contains(sourceEventName, StringComparer.Ordinal))
            return;

        await RouteExtensionEnvelopeAsync(entry, parsed, "Plugin extension event accepted.").ConfigureAwait(false);
    }

    private static string? GetPluginId(PluginExtensionProtocolParseResult parsed) =>
        parsed.StatePatch?.Plugin ??
        parsed.StateSnapshot?.Plugin ??
        parsed.Event?.Plugin ??
        parsed.Error?.Plugin ??
        parsed.Response?.Plugin;

    private async Task OnBuiltInDaemonEventAsync<TData, TMeta>(string daemonEventName, DaemonEvent<TData, TMeta> daemonEvent)
    {
        var extensionEventName = PluginExtensionDaemonEventBridge.ToExtensionEventName(daemonEventName);
        if (extensionEventName is null)
            return;

        var entries = await Dispatcher.InvokeAsync(() => Entries.Select(entry => entry.Entry).ToArray());
        foreach (var entry in entries)
        {
            var parsed = PluginExtensionDaemonEventBridge.CreateBuiltInEventEnvelope(entry, extensionEventName, daemonEvent);
            if (parsed.Ok)
                await RouteExtensionEnvelopeAsync(entry, parsed, $"Daemon event '{extensionEventName}' accepted.").ConfigureAwait(false);
        }
    }

    private async Task RouteExtensionEnvelopeAsync(
        PluginClientExtensionCacheEntry entry,
        PluginExtensionProtocolParseResult parsed,
        string status)
    {
        if (parsed.StatePatch is not null || parsed.StateSnapshot is not null)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                var current = _stateSnapshots.TryGetValue(entry.Id, out var snapshot)
                    ? snapshot
                    : PluginClientExtensionRuntimeHost.CreateState(entry);
                var next = PluginClientExtensionRuntimeHost.ApplyStateEnvelope(entry, current, parsed);
                if (!next.Applied)
                    return;
                _stateSnapshots[entry.Id] = next.Snapshot;
                StatusText.Text = status;
                if (_detailEntry is { } selected && selected.Id == entry.Id)
                    await RenderSelectedAsync(entry);
            });
            return;
        }

        if (parsed.Event is not null)
        {
            var dispatched = await PluginClientExtensionRuntimeHost.DispatchEventAsync(entry, parsed, _ => Task.CompletedTask).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                StatusText.Text = dispatched.Dispatched
                    ? status
                    : string.Join(Environment.NewLine, dispatched.Diagnostics.Select(diagnostic => diagnostic.Message));
            });
        }
    }

    private async Task DispatchUiEventAsync(
        PluginClientExtensionRuntimeHost runtimeHost,
        PluginClientExtensionCacheEntry entry,
        PluginUiEvent uiEvent)
    {
        if (uiEvent.Command is null)
        {
            StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_UiEventReceived"];
            return;
        }

        var dispatched = await runtimeHost.DispatchCommandAsync(entry, uiEvent.Command);
        StatusText.Text = dispatched.Ok
            ? Lang.Tr["Plugin_ExtensionCenter_CommandAccepted"]
            : string.Join(Environment.NewLine, dispatched.Diagnostics.Select(diagnostic => diagnostic.Message));
    }

    private async Task DeployDaemonBundleAsync(PluginClientExtensionCacheEntry entry)
    {
        var daemonBundle = entry.DeploymentPlan.Daemon?.Plugin;
        if (daemonBundle is null)
            return;

        var daemon = GetReadyDaemon();
        if (daemon is null)
        {
            StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_DaemonUnavailable"];
            return;
        }

        if (!entry.CachedFiles.TryGetValue(daemonBundle.Path, out var cachedBundlePath) || !File.Exists(cachedBundlePath))
        {
            StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_DaemonBundleMissing"];
            return;
        }

        SetDeploymentButtons(enabled: false);
        StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_DaemonDeploying"];
        try
        {
            var actualHash = await Sha256FileAsync(cachedBundlePath, CancellationToken.None).ConfigureAwait(false);
            if (!StringComparer.Ordinal.Equals(actualHash, daemonBundle.Sha256))
            {
                await Dispatcher.InvokeAsync(() => StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_DaemonBundleHashMismatch"]);
                return;
            }

            var uploadPath = $"caches/uploads/extensions/{entry.Id}-{entry.Version}.zip";
            await UploadFileToDaemonAsync(daemon, cachedBundlePath, uploadPath, daemonBundle.Sha256, CancellationToken.None).ConfigureAwait(false);
            var deployed = await daemon.InstallExtensionDaemonBundleAsync(
                new ExtensionDaemonBundleInstallRequest(entry.Id, uploadPath, daemonBundle.Sha256),
                CancellationToken.None).ConfigureAwait(false);
            if (deployed.IsErr(out var deployError))
                throw DaemonErrorLocalization.ToException(deployError!);

            var result = deployed.Unwrap();
            await Dispatcher.InvokeAsync(() => StatusText.Text = FormatDeploymentResult(result));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            await Dispatcher.InvokeAsync(() => StatusText.Text = exception.Message);
        }
        finally
        {
            await Dispatcher.InvokeAsync(() => SetDeploymentButtons(enabled: true));
        }
    }

    private async Task SetDaemonBundleEnabledAsync(PluginClientExtensionCacheEntry entry, bool enabled)
    {
        if (entry.DeploymentPlan.Daemon?.Plugin is null)
            return;

        var daemon = GetReadyDaemon();
        if (daemon is null)
        {
            StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_DaemonUnavailable"];
            return;
        }

        SetDeploymentButtons(enabled: false);
        StatusText.Text = Lang.Tr[enabled
            ? "Plugin_ExtensionCenter_DaemonEnabling"
            : "Plugin_ExtensionCenter_DaemonDisabling"];
        try
        {
            var changed = enabled
                ? await daemon.EnableExtensionDaemonBundleAsync(new ExtensionDaemonBundleEnableRequest(entry.Id), CancellationToken.None).ConfigureAwait(false)
                : await daemon.DisableExtensionDaemonBundleAsync(new ExtensionDaemonBundleDisableRequest(entry.Id), CancellationToken.None).ConfigureAwait(false);
            if (changed.IsErr(out var error))
                throw DaemonErrorLocalization.ToException(error!);

            var result = changed.Unwrap();
            await Dispatcher.InvokeAsync(() => StatusText.Text = FormatDeploymentResult(result));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            await Dispatcher.InvokeAsync(() => StatusText.Text = exception.Message);
        }
        finally
        {
            await Dispatcher.InvokeAsync(() => SetDeploymentButtons(enabled: true));
        }
    }

    private async Task RemoveDaemonBundleAsync(PluginClientExtensionCacheEntry entry)
    {
        if (entry.DeploymentPlan.Daemon?.Plugin is null)
            return;

        var daemon = GetReadyDaemon();
        if (daemon is null)
        {
            StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_DaemonUnavailable"];
            return;
        }

        SetDeploymentButtons(enabled: false);
        StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_DaemonRemoving"];
        try
        {
            var removed = await daemon.RemoveExtensionDaemonBundleAsync(
                new ExtensionDaemonBundleRemoveRequest(entry.Id),
                CancellationToken.None).ConfigureAwait(false);
            if (removed.IsErr(out var removeError))
                throw DaemonErrorLocalization.ToException(removeError!);

            var result = removed.Unwrap();
            await Dispatcher.InvokeAsync(() => StatusText.Text = FormatDeploymentResult(result));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            await Dispatcher.InvokeAsync(() => StatusText.Text = exception.Message);
        }
        finally
        {
            await Dispatcher.InvokeAsync(() => SetDeploymentButtons(enabled: true));
        }
    }

    private async Task RequestDaemonShutdownAsync(PluginClientExtensionCacheEntry entry)
    {
        if (entry.DeploymentPlan.Daemon?.Plugin is null)
            return;

        var daemon = GetReadyDaemon();
        if (daemon is null)
        {
            StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_DaemonUnavailable"];
            return;
        }

        var shutdownRequested = false;
        SetDeploymentButtons(enabled: false);
        StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_DaemonShutdownRequesting"];
        try
        {
            var requested = await daemon.RequestDaemonShutdownAsync(
                new DaemonShutdownRequest($"extension:{entry.Id}"),
                CancellationToken.None).ConfigureAwait(false);
            if (requested.IsErr(out var error))
                throw DaemonErrorLocalization.ToException(error!);

            shutdownRequested = true;
            var result = requested.Unwrap();
            var message = string.IsNullOrWhiteSpace(result.Message)
                ? Lang.Tr["Plugin_ExtensionCenter_DaemonShutdownRequested"]
                : result.Message;
            await Dispatcher.InvokeAsync(() => StatusText.Text = message + Environment.NewLine + Lang.Tr["Plugin_ExtensionCenter_DaemonRestartWaitingDisconnect"]);
            _ = TrackDaemonRestartAsync(daemon, entry.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            await Dispatcher.InvokeAsync(() => StatusText.Text = exception.Message);
        }
        finally
        {
            if (!shutdownRequested)
                await Dispatcher.InvokeAsync(() => SetDeploymentButtons(enabled: true));
        }
    }

    private async Task TrackDaemonRestartAsync(DaemonClient.DaemonClient requestedDaemon, string extensionId)
    {
        var disconnected = false;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            if (requestedDaemon.ConnectionState is DaemonConnectionState.Closing or DaemonConnectionState.Closed)
            {
                disconnected = true;
                await Dispatcher.InvokeAsync(() => StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_DaemonRestartWaitingReconnect"]);
                break;
            }
        }

        if (!disconnected)
        {
            await Dispatcher.InvokeAsync(() => StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_DaemonRestartDisconnectNotObserved"]);
            return;
        }

        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            await DaemonsWsManager.CreateAllDaemonWsAsync(CancellationToken.None).ConfigureAwait(false);
            if (GetReadyDaemon() is not null)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusText.Text = string.Format(Lang.Tr["Plugin_ExtensionCenter_DaemonRestartReconnected"], extensionId);
                    SetDeploymentButtons(enabled: true);
                });
                return;
            }
        }

        await Dispatcher.InvokeAsync(() => StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_DaemonRestartStillOffline"]);
    }

    private void SetDeploymentButtons(bool enabled)
    {
        if (_detailEntry is null || _detailEntry.Entry.DeploymentPlan.Daemon?.Plugin is null)
        {
            DeployDaemonMenuItem.IsEnabled = false;
            EnableDaemonMenuItem.IsEnabled = false;
            DisableDaemonMenuItem.IsEnabled = false;
            RequestDaemonRestartMenuItem.IsEnabled = false;
            RemoveDaemonMenuItem.IsEnabled = false;
            return;
        }

        DeployDaemonMenuItem.IsEnabled = enabled;
        EnableDaemonMenuItem.IsEnabled = enabled;
        DisableDaemonMenuItem.IsEnabled = enabled;
        RequestDaemonRestartMenuItem.IsEnabled = enabled;
        RemoveDaemonMenuItem.IsEnabled = enabled;
    }

    private static async Task UploadFileToDaemonAsync(
        DaemonClient.DaemonClient daemon,
        string sourcePath,
        string destinationPath,
        string sha256,
        CancellationToken cancellationToken)
    {
        Guid? sessionId = null;
        var closed = false;
        await using var stream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var openResult = await daemon.Files.OpenUploadAsync(
            new UploadOpenRequest(destinationPath, stream.Length, sha256),
            cancellationToken).ConfigureAwait(false);
        if (openResult.IsErr(out var openError))
            throw DaemonErrorLocalization.ToException(openError!);

        var session = openResult.Unwrap();
        sessionId = session.SessionId;
        try
        {
            var buffer = new byte[session.MaxChunkSize];
            var offset = 0L;
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;

                var writeResult = await daemon.Files.WriteUploadChunkAsync(
                    new UploadChunkRequest(session.SessionId, offset, ImmutableArray.Create(buffer, 0, read)),
                    cancellationToken).ConfigureAwait(false);
                if (writeResult.IsErr(out var writeError))
                    throw DaemonErrorLocalization.ToException(writeError!);

                offset += read;
            }

            var closeResult = await daemon.Files.CloseUploadAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
            if (closeResult.IsErr(out var closeError))
                throw DaemonErrorLocalization.ToException(closeError!);
            closed = true;
        }
        finally
        {
            if (sessionId is { } openedSessionId && !closed)
                _ = await daemon.Files.CancelUploadAsync(openedSessionId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<string> Sha256FileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FormatDeploymentResult(ExtensionDaemonBundleDeploymentResult result)
    {
        var message = string.IsNullOrWhiteSpace(result.Message) ? result.Status : result.Message;
        return result.RestartRequired
            ? message + Environment.NewLine + Lang.Tr["Plugin_ExtensionCenter_DaemonRestartRequired"]
            : message;
    }

    private static async Task<string> FormatMetadataAsync(
        PluginClientExtensionCacheEntry entry,
        PluginExtensionCenterEntrySummary summary)
    {
        var metadata = FormatMetadata(summary);
        var daemon = GetReadyDaemon();
        if (daemon is null)
            return summary.DaemonPluginPath is null
                ? metadata
                : metadata + Environment.NewLine + Lang.Tr["Plugin_ExtensionCenter_DaemonUnavailable"];

        var audit = await FormatExtensionAuditAsync(daemon, entry.Id).ConfigureAwait(false);
        if (summary.DaemonPluginPath is null)
            return metadata + Environment.NewLine + audit;

        var status = await daemon.GetExtensionDaemonBundleStatusAsync(
            new ExtensionDaemonBundleStatusRequest(entry.Id),
            CancellationToken.None).ConfigureAwait(false);
        var deployment = status.IsOk(out var result)
            ? FormatDeploymentResult(result!)
            : DaemonErrorLocalization.GetMessage(status.UnwrapErr());
        return metadata + Environment.NewLine + deployment + Environment.NewLine + audit;
    }

    private static async Task<string> FormatExtensionAuditAsync(DaemonClient.DaemonClient daemon, string extensionId)
    {
        var audit = await daemon.Audit.QueryAsync(
            new AuditQuery(MaximumRecords: 12, Target: extensionId),
            CancellationToken.None).ConfigureAwait(false);
        if (audit.IsErr(out var error))
            return DaemonErrorLocalization.GetMessage(error!);

        var result = audit.Unwrap();
        List<string> lines = [Lang.Tr["Plugin_ExtensionCenter_AuditHistory"]];
        if (result.Records.Length == 0)
        {
            lines.Add(Lang.Tr["Plugin_ExtensionCenter_AuditEmpty"]);
        }
        else
        {
            var methods = result.Records
                .Select(record => record.Method)
                .Distinct(StringComparer.Ordinal)
                .Take(6)
                .ToArray();
            lines.Add(string.Format(Lang.Tr["Plugin_ExtensionCenter_AuditMethods"], JoinForReview(methods)));
            lines.Add(Lang.Tr["Plugin_ExtensionCenter_AuditFailures"]);
            AppendAuditGroup(lines, result.Records.Where(record => !record.Succeeded).Take(4));
            lines.Add(Lang.Tr["Plugin_ExtensionCenter_AuditSuccesses"]);
            AppendAuditGroup(lines, result.Records.Where(record => record.Succeeded).Take(4));
        }

        if (result.DroppedRecords > 0)
            lines.Add(string.Format(Lang.Tr["Plugin_ExtensionCenter_AuditDropped"], result.DroppedRecords));
        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendAuditGroup(List<string> lines, IEnumerable<AuditRecord> records)
    {
        var added = false;
        foreach (var record in records)
        {
            added = true;
            var outcome = record.Succeeded
                ? Lang.Tr["Plugin_ExtensionCenter_AuditOutcomeOk"]
                : string.Format(Lang.Tr["Plugin_ExtensionCenter_AuditOutcomeFailed"], record.ErrorCode ?? "unknown");
            lines.Add(string.Format(
                Lang.Tr["Plugin_ExtensionCenter_AuditRecord"],
                record.Timestamp.ToLocalTime().ToString("u"),
                record.Method,
                outcome));
        }

        if (!added)
            lines.Add(Lang.Tr["Plugin_ExtensionCenter_AuditNone"]);
    }

    private static string FormatMetadata(PluginExtensionCenterEntrySummary summary)
    {
        static string Join(IReadOnlyList<string> values) => values.Count == 0 ? "-" : string.Join(", ", values.Take(8));
        var metadata = string.Format(
            Lang.Tr["Plugin_ExtensionCenter_Metadata"],
            Join(summary.Commands),
            Join(summary.HostCapabilities),
            Join(summary.EventPermissions),
            Join(summary.Resources));
        return string.IsNullOrWhiteSpace(summary.DaemonPluginPath)
            ? metadata
            : metadata + Environment.NewLine + string.Format(
                Lang.Tr["Plugin_ExtensionCenter_DaemonDeploymentPending"],
                summary.DaemonPluginPath);
    }

    private static bool ConfirmInstall(PluginExtensionCenterPackageReview review, string fileName)
    {
        var package = review.Package;
        var daemonPlan = package.DeploymentPlan.Daemon;
        var extensionPoints = daemonPlan?.ExtensionPoints ?? [];
        var resources = ListPackageResources(package);
        var signatureAudit = package.Signature is null
            ? Lang.Tr["Plugin_ExtensionCenter_ReviewSignatureUnsigned"]
            : string.Format(
                Lang.Tr["Plugin_ExtensionCenter_ReviewSignatureTrusted"],
                package.Signature.Publisher,
                package.Signature.KeyId,
                ShortFingerprint(package.Signature.PublicKeySha256),
                package.Signature.SignedAt?.ToString("u") ?? Lang.Tr["Plugin_ExtensionCenter_ReviewSignatureTimeUnknown"]);
        var dependencyAudit = string.Format(
            Lang.Tr["Plugin_ExtensionCenter_ReviewDependencies"],
            JoinForReview(package.Dependencies.Select(dependency => $"{dependency.Id} {dependency.Version}")));
        var updateAudit = string.Format(
            Lang.Tr["Plugin_ExtensionCenter_ReviewUpdates"],
            package.UpdatePolicy is null ? "-" : $"{package.UpdatePolicy.Channel}/{package.UpdatePolicy.Strategy}");
        var riskAudit = FormatRiskReview(package, extensionPoints, daemonPlan?.Plugin is not null);
        var driftAudit = FormatDriftReview(review.Drift);
        var audit = signatureAudit + Environment.NewLine + dependencyAudit + Environment.NewLine + updateAudit + Environment.NewLine + riskAudit + Environment.NewLine + driftAudit + Environment.NewLine + (daemonPlan?.Plugin is not null
            ? Lang.Tr["Plugin_ExtensionCenter_ReviewDaemonAudit"]
            : Lang.Tr["Plugin_ExtensionCenter_ReviewClientAudit"]);
        var message = string.Format(
            Lang.Tr["Plugin_ExtensionCenter_ReviewConfirm"],
            package.PluginId,
            package.Version,
            fileName,
            JoinForReview(package.HostCapabilities),
            JoinForReview(package.EventPermissions),
            JoinForReview(package.Commands.Select(command => command.Id)),
            JoinForReview(extensionPoints.Select(point => $"{point.Kind}:{point.Id}")),
            JoinForReview(resources),
            audit,
            FormatBytes(package.TotalUncompressedBytes));
        return MessageBox.Show(
            message,
            Lang.Tr["Plugin_ExtensionCenter_ReviewTitle"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private static string ShortFingerprint(string fingerprint) =>
        fingerprint.Length <= 24 ? fingerprint : fingerprint[..12] + "..." + fingerprint[^8..];

    private static string FormatDriftReview(IReadOnlyList<PluginClientExtensionManifestDrift> drift)
    {
        if (drift.Count == 0)
            return Lang.Tr["Plugin_ExtensionCenter_ReviewDriftNone"];

        return Lang.Tr["Plugin_ExtensionCenter_ReviewDriftSummary"] + Environment.NewLine + string.Join(Environment.NewLine,
            drift.Take(8).Select(item => string.Format(
                Lang.Tr["Plugin_ExtensionCenter_ReviewDriftItem"],
                item.Level,
                item.Title,
                item.Details)));
    }

    private static string FormatRiskReview(
        ValidatedPluginMpxPackage package,
        IReadOnlyList<PluginMpxExtensionPoint> extensionPoints,
        bool hasDaemonPayload)
    {
        var hostRisk = package.HostCapabilities.Any(capability => capability.StartsWith("daemon.", StringComparison.Ordinal))
            ? Lang.Tr["Plugin_ExtensionCenter_RiskMedium"]
            : Lang.Tr["Plugin_ExtensionCenter_RiskLow"];
        var eventRisk = package.EventPermissions.Count > 0
            ? Lang.Tr["Plugin_ExtensionCenter_RiskMedium"]
            : Lang.Tr["Plugin_ExtensionCenter_RiskLow"];
        var daemonPayloadRisk = hasDaemonPayload
            ? Lang.Tr["Plugin_ExtensionCenter_RiskHigh"]
            : Lang.Tr["Plugin_ExtensionCenter_RiskLow"];
        var extensionPointRisk = extensionPoints.Any(point => point.Kind == "override")
            ? Lang.Tr["Plugin_ExtensionCenter_RiskHigh"]
            : extensionPoints.Any(point => point.Target == "daemon")
                ? Lang.Tr["Plugin_ExtensionCenter_RiskMedium"]
                : Lang.Tr["Plugin_ExtensionCenter_RiskLow"];
        var commandRisk = package.Commands.Count > 0
            ? Lang.Tr["Plugin_ExtensionCenter_RiskMedium"]
            : Lang.Tr["Plugin_ExtensionCenter_RiskLow"];
        var dependencyRisk = package.Dependencies.Count > 0
            ? Lang.Tr["Plugin_ExtensionCenter_RiskMedium"]
            : Lang.Tr["Plugin_ExtensionCenter_RiskLow"];
        var signatureRisk = package.Signature is null
            ? Lang.Tr["Plugin_ExtensionCenter_RiskMedium"]
            : Lang.Tr["Plugin_ExtensionCenter_RiskLow"];

        return Lang.Tr["Plugin_ExtensionCenter_ReviewRiskSummary"] + Environment.NewLine + string.Join(Environment.NewLine,
        [
            string.Format(Lang.Tr["Plugin_ExtensionCenter_ReviewHostRisk"], hostRisk, JoinForReview(package.HostCapabilities)),
            string.Format(Lang.Tr["Plugin_ExtensionCenter_ReviewEventRisk"], eventRisk, JoinForReview(package.EventPermissions)),
            string.Format(Lang.Tr["Plugin_ExtensionCenter_ReviewDaemonPayloadRisk"], daemonPayloadRisk, hasDaemonPayload ? Lang.Tr["Plugin_ExtensionCenter_ReviewDaemonPayloadPresent"] : "-"),
            string.Format(Lang.Tr["Plugin_ExtensionCenter_ReviewExtensionPointRisk"], extensionPointRisk, JoinForReview(extensionPoints.Select(point => $"{point.Kind}:{point.Id}"))),
            string.Format(Lang.Tr["Plugin_ExtensionCenter_ReviewCommandRisk"], commandRisk, JoinForReview(package.Commands.Select(command => command.Id))),
            string.Format(Lang.Tr["Plugin_ExtensionCenter_ReviewDependencyRisk"], dependencyRisk, JoinForReview(package.Dependencies.Select(dependency => $"{dependency.Id} {dependency.Version}"))),
            string.Format(Lang.Tr["Plugin_ExtensionCenter_ReviewSignatureRisk"], signatureRisk, package.Signature is null ? Lang.Tr["Plugin_ExtensionCenter_ReviewSignatureUnsigned"] : string.Format(Lang.Tr["Plugin_ExtensionCenter_ReviewSignatureProvenance"], package.Signature.Publisher, package.Signature.KeyId, ShortFingerprint(package.Signature.PublicKeySha256)))
        ]);
    }

    private static IReadOnlyList<string> ListPackageResources(ValidatedPluginMpxPackage package)
    {
        List<string> resources = [];
        var client = package.DeploymentPlan.Client;
        var daemon = package.DeploymentPlan.Daemon;
        if (daemon?.Plugin is not null)
            resources.Add(daemon.Plugin.Path);
        if (client?.Ui is not null)
            resources.Add(client.Ui.Path);
        if (client?.Theme is not null)
            resources.Add(client.Theme.Path);
        if (client?.Script is not null)
            resources.Add(client.Script.Path);
        if (client is not null)
            resources.AddRange(client.Resources.Select(resource => resource.Path));
        return resources.Order(StringComparer.Ordinal).ToArray();
    }

    private static string JoinForReview(IEnumerable<string> values)
    {
        var materialized = values.Where(value => !string.IsNullOrWhiteSpace(value)).Take(8).ToArray();
        return materialized.Length == 0 ? "-" : string.Join(", ", materialized);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0)
            return "-";
        if (bytes < 1024)
            return $"{bytes} B";
        var kib = bytes / 1024d;
        if (kib < 1024)
            return $"{kib:0.0} KiB";
        return $"{kib / 1024d:0.0} MiB";
    }
}

public enum PluginExtensionScopeKind
{
    ClientOnly,
    ServerOnly,
    Both
}

public sealed class PluginExtensionCenterEntryViewModel
{
    public PluginExtensionCenterEntryViewModel(PluginClientExtensionCacheEntry entry)
    {
        Entry = entry;
        var summary = PluginExtensionCenterService.Summarize(entry);
        Summary = string.Format(
            Lang.Tr["Plugin_ExtensionCenter_ListSummary"],
            entry.Version,
            summary.HasUi ? "UI" : Lang.Tr["Plugin_ExtensionCenter_AssetsOnly"],
            summary.Commands.Count,
            summary.Resources.Count);
        HasUi = summary.HasUi;
        CommandsCount = summary.Commands.Count;
        ResourcesCount = summary.Resources.Count;
        HasDaemonPayload = entry.DeploymentPlan.Daemon?.Plugin is not null;
        ScopeKind = ResolveScopeKind(entry);
    }

    public PluginClientExtensionCacheEntry Entry { get; }

    public string Id => Entry.Id;

    public string Version => Entry.Version;

    public string Summary { get; }

    public bool HasUi { get; }

    public bool HasDaemonPayload { get; }

    public int CommandsCount { get; }

    public int ResourcesCount { get; }

    public PluginExtensionScopeKind ScopeKind { get; }

    public string ScopeKindTag => ScopeKind switch
    {
        PluginExtensionScopeKind.ClientOnly => "client",
        PluginExtensionScopeKind.ServerOnly => "server",
        _ => "both",
    };

    public string ScopeKindText => ScopeKind switch
    {
        PluginExtensionScopeKind.ClientOnly => Lang.Tr["Plugin_ExtensionCenter_ScopeKindClientOnly"],
        PluginExtensionScopeKind.ServerOnly => Lang.Tr["Plugin_ExtensionCenter_ScopeKindServerOnly"],
        _ => Lang.Tr["Plugin_ExtensionCenter_ScopeKindBoth"],
    };

    public string ScopeKindDescription => ScopeKind switch
    {
        PluginExtensionScopeKind.ClientOnly => Lang.Tr["Plugin_ExtensionCenter_ScopeKindClientOnlyDesc"],
        PluginExtensionScopeKind.ServerOnly => Lang.Tr["Plugin_ExtensionCenter_ScopeKindServerOnlyDesc"],
        _ => Lang.Tr["Plugin_ExtensionCenter_ScopeKindBothDesc"],
    };

    public string KindBadge => HasUi
        ? Lang.Tr["Plugin_ExtensionCenter_AssetsHasUi"]
        : Lang.Tr["Plugin_ExtensionCenter_AssetsOnly"];

    public string MetaSummary => $"v{Version} · {CommandsCount} {Lang.Tr["Plugin_ExtensionCenter_MetaCommands"]} · {ResourcesCount} {Lang.Tr["Plugin_ExtensionCenter_MetaResources"]}";

    private static PluginExtensionScopeKind ResolveScopeKind(PluginClientExtensionCacheEntry entry)
    {
        var scopes = entry.DeploymentPlan.Scopes;
        var hasClient = scopes.Contains("client", StringComparer.Ordinal) || scopes.Contains("Client", StringComparer.Ordinal);
        var hasServer = scopes.Contains("daemon", StringComparer.Ordinal) || scopes.Contains("Daemon", StringComparer.Ordinal)
            || entry.DeploymentPlan.Daemon?.Plugin is not null;
        return (hasClient, hasServer) switch
        {
            (true, true) => PluginExtensionScopeKind.Both,
            (true, false) => PluginExtensionScopeKind.ClientOnly,
            _ => PluginExtensionScopeKind.ServerOnly,
        };
    }
}

public sealed class PluginMarketplaceItemViewModel
{
    public PluginMarketplaceItemViewModel(PluginRegistryPluginSummary plugin)
    {
        Id = plugin.Id;
        DisplayName = plugin.DisplayName;
        Summary = plugin.Summary;
        LatestVersion = plugin.LatestVersion;
        var badges = new List<string>();
        if (plugin.TargetsClient)
            badges.Add(Lang.Tr["Plugin_ExtensionCenter_MarketplaceClientBadge"]);
        if (plugin.TargetsDaemon)
            badges.Add(Lang.Tr["Plugin_ExtensionCenter_MarketplaceDaemonBadge"]);
        Meta = string.Format(
            Lang.Tr["Plugin_ExtensionCenter_MarketplaceListSummary"],
            badges.Count > 0 ? string.Join("+", badges) : Lang.Tr["Plugin_ExtensionCenter_AssetsOnly"],
            plugin.LatestVersion ?? "?",
            plugin.Downloads);
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string Summary { get; }

    public string? LatestVersion { get; }

    public string Meta { get; }
}


public sealed class PluginTrustRootViewModel
{
    public PluginTrustRootViewModel(PluginTrustRoot root)
    {
        Publisher = root.Publisher;
        KeyId = root.KeyId;
        Fingerprint = root.PublicKeySha256;
    }

    public string Publisher { get; }

    public string KeyId { get; }

    public string Fingerprint { get; }
}


public sealed class PluginMarketplaceUpdateViewModel
{
    public PluginMarketplaceUpdateViewModel(string id, string currentVersion, string latestVersion, string? changelog)
    {
        Id = id;
        CurrentVersion = currentVersion;
        LatestVersion = latestVersion;
        Changelog = changelog;
    }

    public string Id { get; }

    public string CurrentVersion { get; }

    public string LatestVersion { get; }

    public string? Changelog { get; }
}
