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

    public PluginExtensionCenterPage()
    {
        InitializeComponent();
        DataContext = this;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                RestoreEntries();
            else
                _ = ClearDaemonEventSubscriptionsAsync();
        };
    }

    public ObservableCollection<PluginExtensionCenterEntryViewModel> Entries { get; } = [];

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

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (ExtensionList.SelectedItem is not PluginExtensionCenterEntryViewModel selected)
            return;

        var removed = _service.Uninstall(selected.Entry.Id);
        RestoreEntries();
        StatusText.Text = removed
            ? Lang.Tr["Plugin_ExtensionCenter_RemoveSuccess"]
            : Lang.Tr["Plugin_ExtensionCenter_RemoveMissing"];
    }

    private async void DeployDaemon_Click(object sender, RoutedEventArgs e)
    {
        if (ExtensionList.SelectedItem is not PluginExtensionCenterEntryViewModel selected)
            return;

        await DeployDaemonBundleAsync(selected.Entry).ConfigureAwait(false);
    }

    private async void EnableDaemon_Click(object sender, RoutedEventArgs e)
    {
        if (ExtensionList.SelectedItem is not PluginExtensionCenterEntryViewModel selected)
            return;

        await SetDaemonBundleEnabledAsync(selected.Entry, enabled: true).ConfigureAwait(false);
    }

    private async void DisableDaemon_Click(object sender, RoutedEventArgs e)
    {
        if (ExtensionList.SelectedItem is not PluginExtensionCenterEntryViewModel selected)
            return;

        await SetDaemonBundleEnabledAsync(selected.Entry, enabled: false).ConfigureAwait(false);
    }

    private async void RequestDaemonRestart_Click(object sender, RoutedEventArgs e)
    {
        if (ExtensionList.SelectedItem is not PluginExtensionCenterEntryViewModel selected)
            return;

        await RequestDaemonShutdownAsync(selected.Entry).ConfigureAwait(false);
    }

    private async void RemoveDaemon_Click(object sender, RoutedEventArgs e)
    {
        if (ExtensionList.SelectedItem is not PluginExtensionCenterEntryViewModel selected)
            return;

        await RemoveDaemonBundleAsync(selected.Entry).ConfigureAwait(false);
    }

    private void ExtensionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExtensionList.SelectedItem is PluginExtensionCenterEntryViewModel selected)
            _ = RenderSelectedAsync(selected.Entry);
        else
            ClearSelection();
    }

    private void RestoreEntries(string? selectedId = null)
    {
        var entries = _service.Restore();
        Entries.Clear();
        foreach (var entry in entries)
            Entries.Add(new PluginExtensionCenterEntryViewModel(entry));

        PruneStateSnapshots();
        _ = RefreshDaemonEventSubscriptionsAsync();

        if (Entries.Count == 0)
        {
            ClearSelection();
            StatusText.Text = Lang.Tr["Plugin_ExtensionCenter_Empty"];
            return;
        }

        ExtensionList.SelectedItem = Entries.FirstOrDefault(entry => entry.Id == selectedId) ?? Entries[0];
    }

    private async Task RenderSelectedAsync(PluginClientExtensionCacheEntry entry)
    {
        RemoveButton.IsEnabled = true;
        DeployDaemonButton.IsEnabled = entry.DeploymentPlan.Daemon?.Plugin is not null;
        EnableDaemonButton.IsEnabled = entry.DeploymentPlan.Daemon?.Plugin is not null;
        DisableDaemonButton.IsEnabled = entry.DeploymentPlan.Daemon?.Plugin is not null;
        RequestDaemonRestartButton.IsEnabled = entry.DeploymentPlan.Daemon?.Plugin is not null;
        RemoveDaemonButton.IsEnabled = entry.DeploymentPlan.Daemon?.Plugin is not null;
        var summary = PluginExtensionCenterService.Summarize(entry);
        SelectedTitle.Text = entry.Id;
        SelectedSubtitle.Text = string.Format(
            Lang.Tr["Plugin_ExtensionCenter_SelectedSummary"],
            entry.Version,
            summary.Commands.Count,
            summary.Resources.Count,
            summary.HostCapabilities.Count);

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

    private void ClearSelection()
    {
        RemoveButton.IsEnabled = false;
        DeployDaemonButton.IsEnabled = false;
        EnableDaemonButton.IsEnabled = false;
        DisableDaemonButton.IsEnabled = false;
        RequestDaemonRestartButton.IsEnabled = false;
        RemoveDaemonButton.IsEnabled = false;
        SelectedTitle.Text = Lang.Tr["Plugin_ExtensionCenter_NoSelection"];
        SelectedSubtitle.Text = string.Empty;
        RenderHost.Content = null;
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
                if (ExtensionList.SelectedItem is PluginExtensionCenterEntryViewModel selected && selected.Id == entry.Id)
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
        if (ExtensionList.SelectedItem is not PluginExtensionCenterEntryViewModel selected || selected.Entry.DeploymentPlan.Daemon?.Plugin is null)
        {
            DeployDaemonButton.IsEnabled = false;
            EnableDaemonButton.IsEnabled = false;
            DisableDaemonButton.IsEnabled = false;
            RequestDaemonRestartButton.IsEnabled = false;
            RemoveDaemonButton.IsEnabled = false;
            return;
        }

        DeployDaemonButton.IsEnabled = enabled;
        EnableDaemonButton.IsEnabled = enabled;
        DisableDaemonButton.IsEnabled = enabled;
        RequestDaemonRestartButton.IsEnabled = enabled;
        RemoveDaemonButton.IsEnabled = enabled;
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
        if (summary.DaemonPluginPath is null)
            return metadata;

        var daemon = GetReadyDaemon();
        if (daemon is null)
            return metadata + Environment.NewLine + Lang.Tr["Plugin_ExtensionCenter_DaemonUnavailable"];

        var status = await daemon.GetExtensionDaemonBundleStatusAsync(
            new ExtensionDaemonBundleStatusRequest(entry.Id),
            CancellationToken.None).ConfigureAwait(false);
        return status.IsOk(out var result)
            ? metadata + Environment.NewLine + FormatDeploymentResult(result!)
            : metadata + Environment.NewLine + DaemonErrorLocalization.GetMessage(status.UnwrapErr());
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
                package.Signature.KeyId);
        var dependencyAudit = string.Format(
            Lang.Tr["Plugin_ExtensionCenter_ReviewDependencies"],
            JoinForReview(package.Dependencies.Select(dependency => $"{dependency.Id} {dependency.Version}")));
        var updateAudit = string.Format(
            Lang.Tr["Plugin_ExtensionCenter_ReviewUpdates"],
            package.UpdatePolicy is null ? "-" : $"{package.UpdatePolicy.Channel}/{package.UpdatePolicy.Strategy}");
        var audit = signatureAudit + Environment.NewLine + dependencyAudit + Environment.NewLine + updateAudit + Environment.NewLine + (daemonPlan?.Plugin is not null
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
    }

    public PluginClientExtensionCacheEntry Entry { get; }

    public string Id => Entry.Id;

    public string Summary { get; }
}
