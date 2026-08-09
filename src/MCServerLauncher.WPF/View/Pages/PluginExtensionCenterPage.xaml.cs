using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.Features.Plugins;
using MCServerLauncher.WPF.Modules;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        var result = _service.InstallPackage(dialog.FileName);
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
            StatusText.Text = FormatMetadata(summary);
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
                    : FormatMetadata(summary);
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

    private static string FormatMetadata(PluginExtensionCenterEntrySummary summary)
    {
        static string Join(IReadOnlyList<string> values) => values.Count == 0 ? "-" : string.Join(", ", values.Take(8));
        return string.Format(
            Lang.Tr["Plugin_ExtensionCenter_Metadata"],
            Join(summary.Commands),
            Join(summary.HostCapabilities),
            Join(summary.EventPermissions),
            Join(summary.Resources));
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
