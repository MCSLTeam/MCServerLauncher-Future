using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Daemon.API.State;
using TypedDaemonClient = MCServerLauncher.DaemonClient.DaemonClient;

namespace MCServerLauncher.WPF.Features.Plugins;

public static class PluginUiHostCapabilityNames
{
    public const string UiState = "ui.state";
    public const string DaemonInstanceQuery = "daemon.instance.query";
    public const string DaemonEventSubscribe = "daemon.event.subscribe";
}

public sealed record PluginUiInstanceCatalogView(long Version, IReadOnlyList<PluginUiInstanceCatalogItem> Instances);

public sealed record PluginUiInstanceCatalogItem(
    string Id,
    string Name,
    string InstanceType,
    string Version,
    string Status,
    bool ReadyTimedOut);

public static class PluginUiHostEventNames
{
    public const string InstanceCatalogChanged = "daemon.instance.catalog.changed";
    public const string InstanceLog = "daemon.instance.log";
    public const string DaemonReport = "daemon.report";
    public const string Notification = "daemon.notification";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>([
        InstanceCatalogChanged,
        InstanceLog,
        DaemonReport,
        Notification
    ], StringComparer.Ordinal);
}

public sealed record PluginUiHostEventEnvelope(string Name, object? Data, object? Meta = null);

public interface IPluginUiEventBridge
{
    IDisposable Subscribe(string eventName, Func<PluginUiHostEventEnvelope, Task> handler);
}

public sealed class PluginUiEventBridge(Func<string, Func<PluginUiHostEventEnvelope, Task>, IDisposable> subscribe)
    : IPluginUiEventBridge
{
    public static IPluginUiEventBridge Empty { get; } = new PluginUiEventBridge((_, _) => NoopSubscription.Instance);

    public IDisposable Subscribe(string eventName, Func<PluginUiHostEventEnvelope, Task> handler) =>
        subscribe(eventName, handler);

    private sealed class NoopSubscription : IDisposable
    {
        public static NoopSubscription Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

public interface IPluginUiDaemonQueryBridge
{
    Task<object?> QueryInstanceCatalogAsync(CancellationToken cancellationToken = default);
}

public sealed class PluginUiDaemonQueryBridge(Func<CancellationToken, Task<object?>> queryInstanceCatalogAsync)
    : IPluginUiDaemonQueryBridge
{
    public static IPluginUiDaemonQueryBridge Empty { get; } = new PluginUiDaemonQueryBridge(_ => Task.FromResult<object?>(new PluginUiInstanceCatalogView(0, [])));

    public static IPluginUiDaemonQueryBridge FromDaemonClient(TypedDaemonClient daemon)
    {
        ArgumentNullException.ThrowIfNull(daemon);
        return new PluginUiDaemonQueryBridge(_ => Task.FromResult<object?>(ToView(daemon.InstanceCatalog.Current)));
    }

    public Task<object?> QueryInstanceCatalogAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return queryInstanceCatalogAsync(cancellationToken);
    }

    private static PluginUiInstanceCatalogView ToView(PublishedState<InstanceCatalogSnapshot> published)
    {
        var items = published.Value.Instances.Values
            .OrderBy(instance => instance.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(instance => instance.Id)
            .Select(instance => new PluginUiInstanceCatalogItem(
                instance.Id.ToString("D"),
                instance.Name,
                instance.InstanceType.ToString(),
                instance.Version,
                instance.Status.ToString(),
                instance.ReadyTimedOut))
            .ToArray();
        return new PluginUiInstanceCatalogView(published.Version, new ReadOnlyCollection<PluginUiInstanceCatalogItem>(items));
    }
}

public sealed record PluginUiCommandEvent(string Command, IReadOnlyDictionary<string, object?>? Params = null);

public sealed record PluginUiEvent(string? Handler, string Kind, object? Value = null, PluginUiCommandEvent? Command = null);

public sealed record PluginUiHostLogEntry(PluginUiHostLogLevel Level, string Message);

public sealed record PluginUiHostAuditEntry(
    string Call,
    string? Capability,
    PluginUiHostCallOutcome Outcome,
    string? Code,
    TimeSpan Duration);

public enum PluginUiHostCallOutcome
{
    Ok,
    Denied,
    Timeout,
    Error
}

public sealed class PluginUiHostCallException(string code, string call, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;

    public string Call { get; } = call;
}

public enum PluginUiHostLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public interface IPluginUiHost
{
    void Emit(PluginUiEvent uiEvent);
}

public sealed class PluginUiStateHost(
    Action<PluginUiEvent> emit,
    Action<IReadOnlyDictionary<string, object?>> setState,
    Func<CancellationToken, Task<object?>> queryInstanceCatalogAsync,
    Action<PluginUiHostLogEntry>? log = null,
    IReadOnlySet<string>? hostCapabilities = null,
    Action<PluginUiHostAuditEntry>? audit = null,
    TimeSpan queryTimeout = default,
    IPluginUiEventBridge? eventBridge = null,
    IReadOnlySet<string>? hostEvents = null)
    : IPluginUiHost
{
    private const int MaxLogMessageLength = 2048;
    private static readonly TimeSpan DefaultQueryTimeout = TimeSpan.FromMilliseconds(250);
    private readonly IReadOnlySet<string> _hostCapabilities = hostCapabilities ?? new HashSet<string>(StringComparer.Ordinal);
    private readonly IReadOnlySet<string> _hostEvents = hostEvents ?? new HashSet<string>(StringComparer.Ordinal);
    private readonly TimeSpan _queryTimeout = queryTimeout == default ? DefaultQueryTimeout : queryTimeout;
    private readonly IPluginUiEventBridge _eventBridge = eventBridge ?? PluginUiEventBridge.Empty;

    public void Emit(PluginUiEvent uiEvent) => emit(uiEvent);

    public void Log(PluginUiHostLogLevel level, string message)
    {
        InvokeSync("log", null, () =>
        {
            ArgumentNullException.ThrowIfNull(message);
            if (message.Length > MaxLogMessageLength)
                message = message[..MaxLogMessageLength];
            log?.Invoke(new PluginUiHostLogEntry(level, message));
        });
    }

    public void SetState(IReadOnlyDictionary<string, object?> patch)
    {
        InvokeSync("setState", PluginUiHostCapabilityNames.UiState, () =>
        {
            EnsureCapability(PluginUiHostCapabilityNames.UiState);
            setState(patch);
        });
    }

    public Task<object?> QueryInstanceCatalogAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync(
            "queryInstanceCatalog",
            PluginUiHostCapabilityNames.DaemonInstanceQuery,
            cancellationToken,
            token => queryInstanceCatalogAsync(token));

    public IDisposable SubscribeEvent(string eventName, Func<PluginUiHostEventEnvelope, Task> handler) =>
        InvokeSync("subscribeEvent", PluginUiHostCapabilityNames.DaemonEventSubscribe, () =>
        {
            EnsureCapability(PluginUiHostCapabilityNames.DaemonEventSubscribe);
            EnsureEventPermission(eventName);
            return WrapEventSubscription(_eventBridge.Subscribe(eventName, WrapEventHandler(eventName, handler)));
        });

    private void InvokeSync(string call, string? capability, Action action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            action();
            Audit(call, capability, PluginUiHostCallOutcome.Ok, null, stopwatch.Elapsed);
        }
        catch (UnauthorizedAccessException)
        {
            Audit(call, capability, PluginUiHostCallOutcome.Denied, "host_capability_denied", stopwatch.Elapsed);
            throw;
        }
        catch (PluginUiHostCallException ex)
        {
            Audit(call, capability, PluginUiHostCallOutcome.Error, ex.Code, stopwatch.Elapsed);
            throw;
        }
        catch (Exception ex)
        {
            Audit(call, capability, PluginUiHostCallOutcome.Error, "host_callback_failed", stopwatch.Elapsed);
            throw new PluginUiHostCallException("host_callback_failed", call, $"Plugin host call '{call}' failed.", ex);
        }
    }

    private T InvokeSync<T>(string call, string? capability, Func<T> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = action();
            Audit(call, capability, PluginUiHostCallOutcome.Ok, null, stopwatch.Elapsed);
            return result;
        }
        catch (UnauthorizedAccessException)
        {
            Audit(call, capability, PluginUiHostCallOutcome.Denied, "host_capability_denied", stopwatch.Elapsed);
            throw;
        }
        catch (PluginUiHostCallException ex)
        {
            Audit(call, capability, ex.Code == "host_event_denied" ? PluginUiHostCallOutcome.Denied : PluginUiHostCallOutcome.Error, ex.Code, stopwatch.Elapsed);
            throw;
        }
        catch (Exception ex)
        {
            Audit(call, capability, PluginUiHostCallOutcome.Error, "host_callback_failed", stopwatch.Elapsed);
            throw new PluginUiHostCallException("host_callback_failed", call, $"Plugin host call '{call}' failed.", ex);
        }
    }

    private async Task<object?> InvokeAsync(
        string call,
        string capability,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<object?>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            EnsureCapability(capability);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var task = action(timeout.Token);
            var completed = await Task.WhenAny(task, Task.Delay(_queryTimeout, CancellationToken.None)).ConfigureAwait(false);
            if (completed != task)
            {
                timeout.Cancel();
                ObserveFaults(task);
                Audit(call, capability, PluginUiHostCallOutcome.Timeout, "host_call_timeout", stopwatch.Elapsed);
                throw new PluginUiHostCallException("host_call_timeout", call, $"Plugin host call '{call}' exceeded {_queryTimeout.TotalMilliseconds:0} ms.");
            }

            var result = await task.ConfigureAwait(false);
            Audit(call, capability, PluginUiHostCallOutcome.Ok, null, stopwatch.Elapsed);
            return result;
        }
        catch (UnauthorizedAccessException)
        {
            Audit(call, capability, PluginUiHostCallOutcome.Denied, "host_capability_denied", stopwatch.Elapsed);
            throw;
        }
        catch (PluginUiHostCallException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Audit(call, capability, PluginUiHostCallOutcome.Timeout, "host_call_timeout", stopwatch.Elapsed);
            throw new PluginUiHostCallException("host_call_timeout", call, $"Plugin host call '{call}' was cancelled by its timeout.");
        }
        catch (Exception ex)
        {
            Audit(call, capability, PluginUiHostCallOutcome.Error, "host_callback_failed", stopwatch.Elapsed);
            throw new PluginUiHostCallException("host_callback_failed", call, $"Plugin host call '{call}' failed.", ex);
        }
    }

    private Func<PluginUiHostEventEnvelope, Task> WrapEventHandler(string eventName, Func<PluginUiHostEventEnvelope, Task> handler) => async envelope =>
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!StringComparer.Ordinal.Equals(envelope.Name, eventName))
                throw new PluginUiHostCallException("host_event_denied", "dispatchEvent", $"Host event '{envelope.Name}' does not match subscription '{eventName}'.");

            var task = handler(envelope) ?? Task.CompletedTask;
            var completed = await Task.WhenAny(task, Task.Delay(_queryTimeout, CancellationToken.None)).ConfigureAwait(false);
            if (completed != task)
            {
                ObserveFaults(task);
                throw new PluginUiHostCallException("host_call_timeout", "dispatchEvent", $"Plugin host event handler exceeded {_queryTimeout.TotalMilliseconds:0} ms.");
            }

            await task.ConfigureAwait(false);
            Audit("dispatchEvent", PluginUiHostCapabilityNames.DaemonEventSubscribe, PluginUiHostCallOutcome.Ok, null, stopwatch.Elapsed);
        }
        catch (PluginUiHostCallException ex)
        {
            Audit("dispatchEvent", PluginUiHostCapabilityNames.DaemonEventSubscribe, OutcomeForCode(ex.Code), ex.Code, stopwatch.Elapsed);
            throw;
        }
        catch (Exception ex)
        {
            Audit("dispatchEvent", PluginUiHostCapabilityNames.DaemonEventSubscribe, PluginUiHostCallOutcome.Error, "host_callback_failed", stopwatch.Elapsed);
            throw new PluginUiHostCallException("host_callback_failed", "dispatchEvent", "Plugin host event handler failed.", ex);
        }
    };

    private IDisposable WrapEventSubscription(IDisposable subscription) => new AuditedPluginUiEventSubscription(subscription, DisposeEventSubscription);

    private void DisposeEventSubscription(IDisposable subscription)
    {
        InvokeSync("unsubscribeEvent", PluginUiHostCapabilityNames.DaemonEventSubscribe, subscription.Dispose);
    }

    private static PluginUiHostCallOutcome OutcomeForCode(string code) => code switch
    {
        "host_capability_denied" or "host_event_denied" => PluginUiHostCallOutcome.Denied,
        "host_call_timeout" => PluginUiHostCallOutcome.Timeout,
        _ => PluginUiHostCallOutcome.Error
    };

    private static void ObserveFaults(Task task)
    {
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private void Audit(
        string call,
        string? capability,
        PluginUiHostCallOutcome outcome,
        string? code,
        TimeSpan duration)
    {
        audit?.Invoke(new PluginUiHostAuditEntry(call, capability, outcome, code, duration));
    }

    private void EnsureCapability(string capability)
    {
        if (!_hostCapabilities.Contains(capability))
            throw new UnauthorizedAccessException($"Host capability '{capability}' was not declared by this plugin package.");
    }

    private void EnsureEventPermission(string eventName)
    {
        if (!PluginUiHostEventNames.Allowed.Contains(eventName) || !_hostEvents.Contains(eventName))
            throw new PluginUiHostCallException("host_event_denied", "subscribeEvent", $"Host event '{eventName}' was not declared by this plugin package.");
    }

    private sealed class AuditedPluginUiEventSubscription(IDisposable subscription, Action<IDisposable> dispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            dispose(subscription);
        }
    }
}

public static partial class PluginUiBinding
{
    private const int MaxPathSegments = 12;

    public static object? ResolveValue(object? value, IReadOnlyDictionary<string, object?> state)
    {
        if (value is not string text)
            return value;

        var stateMatch = StateBindingRegex().Match(text);
        if (stateMatch.Success)
            return ReadStatePath(state, stateMatch.Groups[1].Value.Split('.'));

        var formatMatch = FormatBindingRegex().Match(text);
        if (formatMatch.Success)
            return FormatValue(formatMatch.Groups[1].Value, ReadStatePath(state, formatMatch.Groups[2].Value.Split('.')));

        return InterpolateText(text, state);
    }

    public static string ResolveText(object? value, IReadOnlyDictionary<string, object?> state)
    {
        var resolved = ResolveValue(value, state);
        return resolved switch
        {
            null => string.Empty,
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => resolved.ToString() ?? string.Empty
        };
    }

    public static bool ResolveBoolean(object? value, IReadOnlyDictionary<string, object?> state)
    {
        return ResolveValue(value, state) switch
        {
            bool boolean => boolean,
            string text => bool.TryParse(text, out var parsed) && parsed,
            _ => false
        };
    }

    public static bool IsAllowedBinding(string value)
    {
        var binding = StateBindingRegex().Match(value);
        if (binding.Success)
            return IsAllowedPath(binding.Groups[1].Value);

        binding = FormatBindingRegex().Match(value);
        return binding.Success && IsAllowedPath(binding.Groups[2].Value);
    }

    private static string InterpolateText(string text, IReadOnlyDictionary<string, object?> state)
    {
        var withState = InlineStateBindingRegex().Replace(text, match =>
            ToText(ReadStatePath(state, match.Groups[1].Value.Split('.'))));
        return InlineFormatBindingRegex().Replace(withState, match =>
            FormatValue(match.Groups[1].Value, ReadStatePath(state, match.Groups[2].Value.Split('.'))));
    }

    private static object? ReadStatePath(IReadOnlyDictionary<string, object?> state, IReadOnlyList<string> path)
    {
        object? current = state;
        foreach (var segment in path)
        {
            if (current is IReadOnlyDictionary<string, object?> dictionary && dictionary.TryGetValue(segment, out var next))
            {
                current = next;
                continue;
            }

            return null;
        }

        return current;
    }

    private static bool IsAllowedPath(string path)
    {
        var segments = path.Split('.');
        return segments.Length <= MaxPathSegments && segments.All(segment => StatePathSegmentRegex().IsMatch(segment));
    }

    private static string FormatValue(string formatter, object? value)
    {
        return formatter switch
        {
            "percent" when value is double number => $"{number * 100:0.0}%",
            "percent" when value is float number => $"{number * 100:0.0}%",
            "percent" when value is decimal number => $"{number * 100:0.0}%",
            "number" when value is IFormattable number => number.ToString("N0", CultureInfo.CurrentCulture),
            "bytes" when value is long bytes => FormatBytes(bytes),
            "bytes" when value is int bytes => FormatBytes(bytes),
            "bytes" when value is double bytes => FormatBytes(bytes),
            _ => string.Empty
        };
    }

    private static string FormatBytes(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            return string.Empty;

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var scaled = value;
        var index = 0;
        while (scaled >= 1024 && index < units.Length - 1)
        {
            scaled /= 1024;
            index++;
        }

        return index == 0
            ? $"{scaled:0} {units[index]}"
            : $"{scaled:0.0} {units[index]}";
    }

    private static string ToText(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    [GeneratedRegex(@"^\{state\.([A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)*)\}$", RegexOptions.CultureInvariant)]
    private static partial Regex StateBindingRegex();

    [GeneratedRegex(@"^\{format\.(percent|number|bytes)\(state\.([A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)*)\)\}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatBindingRegex();

    [GeneratedRegex(@"\{state\.([A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex InlineStateBindingRegex();

    [GeneratedRegex(@"\{format\.(percent|number|bytes)\(state\.([A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)*)\)\}", RegexOptions.CultureInvariant)]
    private static partial Regex InlineFormatBindingRegex();

    [GeneratedRegex(@"^[A-Za-z_][\w]*$", RegexOptions.CultureInvariant)]
    private static partial Regex StatePathSegmentRegex();
}
