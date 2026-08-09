using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.WPF.Features.Plugins;
using RustyOptions;

namespace MCServerLauncher.WPF.Tests.Plugins;

public sealed class PluginUiRuntimeTests
{
    [Fact]
    public void ExtensionProtocolParsesAndAppliesStatePatchRevision()
    {
        var parsed = PluginExtensionProtocol.Parse("""
        {
          "protocol": "mcsl.extension.v1",
          "type": "state.patch",
          "plugin": "community.example.status-panel",
          "revision": 2,
          "patch": { "cpu": 37.5, "status": "running" }
        }
        """);

        Assert.True(parsed.Ok, string.Join(Environment.NewLine, parsed.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var next = PluginExtensionProtocol.ApplyStatePatch(
            new PluginExtensionStateSnapshot(
                "community.example.status-panel",
                1,
                new Dictionary<string, object?> { ["status"] = "starting" }),
            parsed.StatePatch!);

        Assert.True(next.Applied);
        Assert.Equal(2, next.Snapshot.Revision);
        Assert.Equal("running", next.Snapshot.State["status"]);
        Assert.Equal(37.5, Assert.IsType<double>(next.Snapshot.State["cpu"]));
    }

    [Fact]
    public void ExtensionProtocolIgnoresStaleStatePatchRevision()
    {
        var patch = new PluginExtensionStatePatchEnvelope(
            PluginExtensionProtocol.Version,
            "state.patch",
            "community.example.status-panel",
            1,
            new Dictionary<string, object?> { ["status"] = "stale" });

        var next = PluginExtensionProtocol.ApplyStatePatch(
            new PluginExtensionStateSnapshot(
                "community.example.status-panel",
                2,
                new Dictionary<string, object?> { ["status"] = "running" }),
            patch);

        Assert.False(next.Applied);
        Assert.Equal(2, next.Snapshot.Revision);
        Assert.Equal("running", next.Snapshot.State["status"]);
    }

    [Fact]
    public void ExtensionProtocolParsesAndAppliesStateSnapshotReplacement()
    {
        var parsed = PluginExtensionProtocol.Parse("""
        {
          "protocol": "mcsl.extension.v1",
          "type": "state.snapshot",
          "plugin": "community.example.status-panel",
          "revision": 4,
          "state": { "status": "ready", "cpu": 0.42 }
        }
        """);

        Assert.True(parsed.Ok, string.Join(Environment.NewLine, parsed.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var next = PluginExtensionProtocol.ApplyStateEnvelope(
            new PluginExtensionStateSnapshot(
                "community.example.status-panel",
                3,
                new Dictionary<string, object?> { ["status"] = "starting", ["memory"] = 512L }),
            parsed);

        Assert.True(next.Applied);
        Assert.Equal(4, next.Snapshot.Revision);
        Assert.Equal("ready", next.Snapshot.State["status"]);
        Assert.Equal(0.42, Assert.IsType<double>(next.Snapshot.State["cpu"]));
        Assert.False(next.Snapshot.State.ContainsKey("memory"));

        var stale = PluginExtensionProtocol.ApplyStateEnvelope(next.Snapshot, PluginExtensionProtocol.Parse("""
        {
          "protocol": "mcsl.extension.v1",
          "type": "state.snapshot",
          "plugin": "community.example.status-panel",
          "revision": 2,
          "state": { "status": "stale" }
        }
        """));
        Assert.False(stale.Applied);
        Assert.Equal("ready", stale.Snapshot.State["status"]);
    }

    [Fact]
    public void ClientExtensionRuntimeHostAppliesOnlyMatchingStateEnvelopes()
    {
        var entry = CreateCacheEntry("community.example.status-panel");
        var state = PluginClientExtensionRuntimeHost.CreateState(
            entry,
            new Dictionary<string, object?> { ["status"] = "starting" },
            revision: 1);
        var parsed = PluginExtensionProtocol.Parse("""
        {
          "protocol": "mcsl.extension.v1",
          "type": "state.patch",
          "plugin": "community.example.status-panel",
          "revision": 2,
          "patch": { "status": "ready" }
        }
        """);

        var next = PluginClientExtensionRuntimeHost.ApplyStateEnvelope(entry, state, parsed);

        Assert.True(next.Applied);
        Assert.Equal("ready", next.Snapshot.State["status"]);

        var foreign = PluginClientExtensionRuntimeHost.ApplyStateEnvelope(entry, next.Snapshot, PluginExtensionProtocol.Parse("""
        {
          "protocol": "mcsl.extension.v1",
          "type": "state.patch",
          "plugin": "community.example.other",
          "revision": 3,
          "patch": { "status": "wrong" }
        }
        """));
        Assert.False(foreign.Applied);
        Assert.Equal("ready", foreign.Snapshot.State["status"]);
    }

    [Fact]
    public void ExtensionProtocolRejectsUnsafeStatePatchKeys()
    {
        var parsed = PluginExtensionProtocol.Parse("""
        {
          "protocol": "mcsl.extension.v1",
          "type": "state.patch",
          "plugin": "community.example.status-panel",
          "revision": 3,
          "patch": { "__proto__": { "polluted": true } }
        }
        """);

        Assert.False(parsed.Ok);
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "json_key_unsafe");
    }

    [Fact]
    public async Task ClientExtensionRuntimeHostDispatchesDeclaredEventsOnly()
    {
        var entry = CreateCacheEntry(
            "community.example.status-panel",
            eventPermissions: ["daemon.instance.log"]);
        var parsed = PluginExtensionProtocol.Parse("""
        {
          "protocol": "mcsl.extension.v1",
          "type": "event",
          "plugin": "community.example.status-panel",
          "name": "daemon.instance.log",
          "version": 1,
          "data": { "line": "hello" }
        }
        """);
        Assert.True(parsed.Ok, string.Join(Environment.NewLine, parsed.Diagnostics.Select(diagnostic => diagnostic.Message)));

        List<string> lines = [];
        var dispatched = await PluginClientExtensionRuntimeHost.DispatchEventAsync(
            entry,
            parsed,
            envelope =>
            {
                lines.Add((string)envelope.Data["line"]!);
                return Task.CompletedTask;
            });

        Assert.True(dispatched.Dispatched, string.Join(Environment.NewLine, dispatched.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(["hello"], lines);

        var undeclared = await PluginClientExtensionRuntimeHost.DispatchEventAsync(
            CreateCacheEntry("community.example.status-panel"),
            parsed,
            _ => throw new InvalidOperationException("must not dispatch"));
        Assert.False(undeclared.Dispatched);
        Assert.Equal("event_undeclared", Assert.Single(undeclared.Diagnostics).Code);
    }

    [Fact]
    public void ExtensionDaemonEventBridgeWrapsDeclaredBuiltInEvents()
    {
        var entry = CreateCacheEntry(
            "community.example.status-panel",
            eventPermissions: ["daemon.notification"]);
        var daemonEvent = new DaemonEvent<NotificationEventData, NotificationEventMeta>(
            7,
            42,
            DaemonEventField<NotificationEventMeta>.FromValue(new NotificationEventMeta(Guid.NewGuid(), Guid.NewGuid())),
            DaemonEventField<NotificationEventData>.FromValue(new NotificationEventData("Ready", "Started", "success")));

        var parsed = PluginExtensionDaemonEventBridge.CreateBuiltInEventEnvelope(
            entry,
            "daemon.notification",
            daemonEvent);

        Assert.True(parsed.Ok, string.Join(Environment.NewLine, parsed.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.NotNull(parsed.Event);
        Assert.Equal("community.example.status-panel", parsed.Event.Plugin);
        Assert.Equal("daemon.notification", parsed.Event.Name);
        Assert.Equal("Ready", parsed.Event.Data["Title"]);
        Assert.Equal("success", parsed.Event.Data["Severity"]);
        Assert.NotNull(parsed.Event.Meta);
        Assert.True(parsed.Event.Meta!.ContainsKey("SourceInstanceId"));
    }

    [Fact]
    public void ExtensionDaemonEventBridgeRejectsUndeclaredBuiltInEvents()
    {
        var entry = CreateCacheEntry("community.example.status-panel");
        var daemonEvent = new DaemonEvent<NotificationEventData, NotificationEventMeta>(
            7,
            42,
            DaemonEventField<NotificationEventMeta>.FromValue(new NotificationEventMeta(Guid.NewGuid(), Guid.NewGuid())),
            DaemonEventField<NotificationEventData>.FromValue(new NotificationEventData("Ready", "Started", "success")));

        var parsed = PluginExtensionDaemonEventBridge.CreateBuiltInEventEnvelope(
            entry,
            "daemon.notification",
            daemonEvent);

        Assert.False(parsed.Ok);
        Assert.Equal("event_undeclared", Assert.Single(parsed.Diagnostics).Code);
    }

    [Fact]
    public void ExtensionDaemonEventBridgeParsesPluginOwnedStateEnvelope()
    {
        var envelope = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["protocol"] = PluginExtensionProtocol.Version,
            ["type"] = "state.patch",
            ["plugin"] = "community.example.status-panel",
            ["revision"] = 4,
            ["patch"] = new Dictionary<string, object?>
            {
                ["title"] = "Ready"
            }
        });
        var daemonEvent = new DaemonEvent<ExtensionProtocolDispatchResult, EmptyRequest>(
            1,
            2,
            DaemonEventField<EmptyRequest>.Missing,
            DaemonEventField<ExtensionProtocolDispatchResult>.FromValue(new ExtensionProtocolDispatchResult(envelope)));

        var parsed = PluginExtensionDaemonEventBridge.ParsePluginExtensionEvent(daemonEvent);

        Assert.True(parsed.Ok, string.Join(Environment.NewLine, parsed.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.NotNull(parsed.StatePatch);
        Assert.Equal("community.example.status-panel", parsed.StatePatch.Plugin);
        Assert.Equal(4, parsed.StatePatch.Revision);
        Assert.Equal("Ready", parsed.StatePatch.Patch["title"]);
    }

    [Fact]
    public void ThemeRuntimeParsesAndMapsDesignTokens()
    {
        var parsed = PluginThemeRuntimeParser.Parse("""
        {
          "schema": "mcsl.theme.v1",
          "colors": {
            "accent": "#0070C0",
            "textPrimary": "#FFFFFF"
          },
          "spacing": { "sm": 4, "md": 8 }
        }
        """);

        Assert.True(parsed.Ok, string.Join(Environment.NewLine, parsed.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal("#0070c0", parsed.Theme!.Colors["accent"]);
        var runtime = PluginThemeRuntimeParser.ParseRuntime(parsed.RuntimeJson!);
        Assert.True(runtime.Ok, string.Join(Environment.NewLine, runtime.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var resources = PluginThemeRuntimeParser.ToResourceDictionary(runtime.Theme!);
        Assert.True(resources.Contains("McslExtensionColorAccent"));
        Assert.Equal(8d, resources["McslExtensionSpacingMd"]);
    }

    [Fact]
    public void ThemeRuntimeRejectsUnsafeTokens()
    {
        var parsed = PluginThemeRuntimeParser.ParseRuntime("""
        {
          "schema": "mcsl.theme.v1",
          "colors": { "accent": "javascript:red", "brand": "#ffffff" },
          "spacing": { "huge": 1000, "md": 12.5 }
        }
        """);

        Assert.False(parsed.Ok);
        Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code is "theme_color_invalid" or "theme_color_unknown" or "theme_spacing_unknown" or "theme_spacing_invalid");
    }

    [Fact]
    public void ExtensionProtocolParsesStructuredErrors()
    {
        var parsed = PluginExtensionProtocol.Parse("""
        {
          "protocol": "mcsl.extension.v1",
          "type": "error",
          "plugin": "community.example.status-panel",
          "code": "instance_not_found",
          "message": "Instance does not exist",
          "details": { "instanceId": "11111111-2222-3333-4444-555555555555" }
        }
        """);

        Assert.True(parsed.Ok, string.Join(Environment.NewLine, parsed.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal("instance_not_found", parsed.Error?.Code);
    }

    [Fact]
    public void ExtensionProtocolCreatesRequestAndParsesResponseEnvelope()
    {
        var request = PluginExtensionProtocol.CreateRequestEnvelope(
            "request-1",
            "community.example.status-panel",
            "refresh",
            new Dictionary<string, object?> { ["mode"] = "fast" });
        Assert.Equal("request", request.GetProperty("type").GetString());
        Assert.Equal("refresh", request.GetProperty("command").GetString());
        Assert.Equal("fast", request.GetProperty("params").GetProperty("mode").GetString());

        var parsed = PluginExtensionProtocol.Parse("""
        {
          "protocol": "mcsl.extension.v1",
          "type": "response",
          "id": "request-1",
          "plugin": "community.example.status-panel",
          "result": { "accepted": true }
        }
        """);

        Assert.True(parsed.Ok, string.Join(Environment.NewLine, parsed.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal("request-1", parsed.Response?.Id);
        var result = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(parsed.Response?.Result);
        Assert.Equal(true, result["accepted"]);
    }

    [Fact]
    public async Task ExtensionCommandDispatcherRoutesUiCommandsThroughDaemonDispatch()
    {
        JsonElement? captured = null;
        var dispatcher = new PluginExtensionCommandDispatcher((request, _) =>
        {
            captured = request.Envelope.Clone();
            var response = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["protocol"] = PluginExtensionProtocol.Version,
                ["type"] = "response",
                ["id"] = "request-1",
                ["plugin"] = "community.example.status-panel",
                ["result"] = new Dictionary<string, object?> { ["accepted"] = true }
            });
            return Task.FromResult(Result.Ok<ExtensionProtocolDispatchResult, DaemonError>(new ExtensionProtocolDispatchResult(response)));
        });

        var parsed = await dispatcher.DispatchAsync(
            "community.example.status-panel",
            new PluginUiCommandEvent("refresh", new Dictionary<string, object?> { ["mode"] = "safe" }),
            "request-1");

        Assert.True(parsed.Ok, string.Join(Environment.NewLine, parsed.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal("response", parsed.Response?.Type);
        Assert.Equal("refresh", captured?.GetProperty("command").GetString());
        Assert.Equal("safe", captured?.GetProperty("params").GetProperty("mode").GetString());
    }

    [Fact]
    public void CompilerEmitsStrictRuntimeJsonFromAuthoringJson5()
    {
        var result = PluginUiSchemaCompiler.CompileToRuntimeJson("""
        {
          // Authoring-only syntax should not survive into the runtime package.
          schema: "mcsl.ui.v1",
          root: {
            type: "View",
            props: { Direction: "Vertical", Gap: "Spacing.Md" },
            children: [{ type: "Text", props: { Text: "{state.title}" } }],
          },
        }
        """);

        Assert.True(result.Ok, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.NotNull(result.RuntimeJson);
        Assert.DoesNotContain("//", result.RuntimeJson);
        var runtime = PluginUiSchemaParser.ParseRuntime(result.RuntimeJson!);
        Assert.True(runtime.Ok, string.Join(Environment.NewLine, runtime.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    [Fact]
    public void ParserAcceptsJson5SchemaWithUnquotedKeysCommentsAndTrailingCommas()
    {
        var result = PluginUiSchemaParser.Parse("""
        {
          // WPF PoC accepts the authoring subset used by Web mock .mpx packages.
          schema: "mcsl.ui.v1",
          root: {
            type: "Card",
            props: { Title: "{state.title}" },
            children: [
              { type: "Text", props: { Text: "CPU {format.percent(state.metrics.cpu)}" } },
              { type: "Button", props: { Text: "Refresh", OnClick: "refresh" } },
            ],
          },
        }
        """);

        Assert.True(result.Ok);
        Assert.NotNull(result.Schema);
        Assert.Equal(PluginUiComponentType.Card, result.Schema.Root.Type);
    }

    [Fact]
    public void ParserAcceptsExtensionCommandEventBindings()
    {
        var result = PluginUiSchemaParser.Parse("""
        {
          schema: "mcsl.ui.v1",
          root: {
            type: "Button",
            props: {
              Text: "Refresh",
              OnClick: {
                Command: "refresh",
                Params: {
                  mode: "{state.mode}",
                  selected: "{event.value}",
                  memory: "{format.bytes(state.metrics.memory)}"
                }
              }
            }
          }
        }
        """);

        Assert.True(result.Ok, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var action = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(result.Schema!.Root.Props["OnClick"]);
        Assert.Equal("refresh", action["Command"]);
        var parameters = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(action["Params"]);
        Assert.Equal("{event.value}", parameters["selected"]);
    }

    [Fact]
    public void ParserRejectsUnsafeExtensionCommandEventBindings()
    {
        var result = PluginUiSchemaParser.Parse("""
        {
          "schema": "mcsl.ui.v1",
          "root": {
            "type": "Button",
            "props": {
              "Text": "Run",
              "OnClick": {
                "Command": "plugin refresh",
                "Extra": true,
                "Params": {
                  "__proto__": "polluted",
                  "value": "{state.items[0]}"
                }
              }
            }
          }
        }
        """);

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code is "command_invalid" or "command_property_unknown" or "command_param_key_unsafe" or "binding_invalid");
    }

    [Fact]
    public void ParserRejectsUnsafePropertyAndIllegalBinding()
    {
        var result = PluginUiSchemaParser.Parse("""
        {
          "schema": "mcsl.ui.v1",
          "root": {
            "type": "Text",
            "props": {
              "Html": "<script>alert(1)</script>",
              "Text": "{state.items[0]}"
            }
          }
        }
        """);

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code is "prop_unknown" or "binding_invalid" or "prop_script_forbidden");
    }

    [Fact]
    public void BindingInterpolatesStateAndFormatters()
    {
        Dictionary<string, object?> state = new()
        {
            ["name"] = "alpha",
            ["metrics"] = new Dictionary<string, object?>
            {
                ["cpu"] = 0.25,
                ["memory"] = 512L * 1024L * 1024L
            }
        };

        Assert.Equal("alpha", PluginUiBinding.ResolveText("{state.name}", state));
        Assert.Equal("CPU 25.0% / 512.0 MB", PluginUiBinding.ResolveText("CPU {format.percent(state.metrics.cpu)} / {format.bytes(state.metrics.memory)}", state));
    }

    [Fact]
    public void HostLogIsBoundedAndRoutedThroughCallback()
    {
        List<PluginUiHostLogEntry> logs = [];
        var host = new PluginUiStateHost(
            _ => { },
            _ => { },
            _ => Task.FromResult<object?>(Array.Empty<object>()),
            logs.Add,
            new HashSet<string>(StringComparer.Ordinal));

        host.Log(PluginUiHostLogLevel.Info, new string('x', 4096));

        var entry = Assert.Single(logs);
        Assert.Equal(PluginUiHostLogLevel.Info, entry.Level);
        Assert.Equal(2048, entry.Message.Length);
    }

    [Fact]
    public void HostRejectsStateMutationWithoutDeclaredCapability()
    {
        var host = new PluginUiStateHost(
            _ => { },
            _ => { },
            _ => Task.FromResult<object?>(Array.Empty<object>()));

        Assert.Throws<UnauthorizedAccessException>(() =>
            host.SetState(new Dictionary<string, object?> { ["title"] = "blocked" }));
    }

    [Fact]
    public async Task HostAllowsInstanceCatalogQueryOnlyWithDeclaredCapability()
    {
        var denied = new PluginUiStateHost(
            _ => { },
            _ => { },
            _ => Task.FromResult<object?>(new PluginUiInstanceCatalogView(1, [])));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.QueryInstanceCatalogAsync());

        var allowed = new PluginUiStateHost(
            _ => { },
            _ => { },
            _ => Task.FromResult<object?>(new PluginUiInstanceCatalogView(2, [])),
            hostCapabilities: new HashSet<string>([PluginUiHostCapabilityNames.DaemonInstanceQuery], StringComparer.Ordinal));

        var result = Assert.IsType<PluginUiInstanceCatalogView>(await allowed.QueryInstanceCatalogAsync());
        Assert.Equal(2, result.Version);
    }

    [Fact]
    public void HostAuditsAllowedAndDeniedCalls()
    {
        List<PluginUiHostAuditEntry> audit = [];
        var host = new PluginUiStateHost(
            _ => { },
            _ => { },
            _ => Task.FromResult<object?>(new PluginUiInstanceCatalogView(1, [])),
            hostCapabilities: new HashSet<string>([PluginUiHostCapabilityNames.UiState], StringComparer.Ordinal),
            audit: audit.Add);

        host.Log(PluginUiHostLogLevel.Info, "hello");
        host.SetState(new Dictionary<string, object?> { ["title"] = "allowed" });
        Assert.Throws<UnauthorizedAccessException>(() =>
            host.QueryInstanceCatalogAsync().GetAwaiter().GetResult());

        Assert.Collection(
            audit,
            entry =>
            {
                Assert.Equal("log", entry.Call);
                Assert.Equal(PluginUiHostCallOutcome.Ok, entry.Outcome);
            },
            entry =>
            {
                Assert.Equal("setState", entry.Call);
                Assert.Equal(PluginUiHostCallOutcome.Ok, entry.Outcome);
            },
            entry =>
            {
                Assert.Equal("queryInstanceCatalog", entry.Call);
                Assert.Equal(PluginUiHostCallOutcome.Denied, entry.Outcome);
                Assert.Equal("host_capability_denied", entry.Code);
            });
    }

    [Fact]
    public async Task HostTimesOutSlowInstanceCatalogQuery()
    {
        List<PluginUiHostAuditEntry> audit = [];
        var host = new PluginUiStateHost(
            _ => { },
            _ => { },
            _ => Task.Delay(Timeout.InfiniteTimeSpan).ContinueWith<object?>(_ => new PluginUiInstanceCatalogView(1, []), TaskScheduler.Default),
            hostCapabilities: new HashSet<string>([PluginUiHostCapabilityNames.DaemonInstanceQuery], StringComparer.Ordinal),
            audit: audit.Add,
            queryTimeout: TimeSpan.FromMilliseconds(5));

        var exception = await Assert.ThrowsAsync<PluginUiHostCallException>(() => host.QueryInstanceCatalogAsync());
        Assert.Equal("host_call_timeout", exception.Code);
        Assert.Contains(audit, entry => entry.Call == "queryInstanceCatalog" && entry.Outcome == PluginUiHostCallOutcome.Timeout && entry.Code == "host_call_timeout");
    }

    [Fact]
    public async Task HostGatesEventSubscriptionByCapabilityAndManifestEvent()
    {
        var deniedCapability = new PluginUiStateHost(
            _ => { },
            _ => { },
            _ => Task.FromResult<object?>(Array.Empty<object>()),
            eventBridge: PluginUiEventBridge.Empty,
            hostEvents: new HashSet<string>([PluginUiHostEventNames.InstanceLog], StringComparer.Ordinal));
        Assert.Throws<UnauthorizedAccessException>(() =>
            deniedCapability.SubscribeEvent(PluginUiHostEventNames.InstanceLog, _ => Task.CompletedTask));

        var deniedEvent = new PluginUiStateHost(
            _ => { },
            _ => { },
            _ => Task.FromResult<object?>(Array.Empty<object>()),
            hostCapabilities: new HashSet<string>([PluginUiHostCapabilityNames.DaemonEventSubscribe], StringComparer.Ordinal),
            eventBridge: PluginUiEventBridge.Empty);
        var denied = Assert.Throws<PluginUiHostCallException>(() =>
            deniedEvent.SubscribeEvent(PluginUiHostEventNames.InstanceLog, _ => Task.CompletedTask));
        Assert.Equal("host_event_denied", denied.Code);

        var envelopes = new List<PluginUiHostEventEnvelope>();
        var disposeCount = 0;
        Func<PluginUiHostEventEnvelope, Task>? registered = null;
        var allowed = new PluginUiStateHost(
            _ => { },
            _ => { },
            _ => Task.FromResult<object?>(Array.Empty<object>()),
            hostCapabilities: new HashSet<string>([PluginUiHostCapabilityNames.DaemonEventSubscribe], StringComparer.Ordinal),
            eventBridge: new PluginUiEventBridge((_, handler) =>
            {
                registered = handler;
                return new DelegateDisposable(() => disposeCount++);
            }),
            hostEvents: new HashSet<string>([PluginUiHostEventNames.InstanceLog], StringComparer.Ordinal));

        using (allowed.SubscribeEvent(PluginUiHostEventNames.InstanceLog, envelope =>
        {
            envelopes.Add(envelope);
            return Task.CompletedTask;
        }))
        {
            Assert.NotNull(registered);
            await registered!(new PluginUiHostEventEnvelope(PluginUiHostEventNames.InstanceLog, new { Line = "hello" }));
        }

        Assert.Equal(1, disposeCount);
        Assert.Equal(PluginUiHostEventNames.InstanceLog, Assert.Single(envelopes).Name);
    }

    [Fact]
    public async Task HostAuditsEventDispatchAndCleanup()
    {
        List<PluginUiHostAuditEntry> audit = [];
        Func<PluginUiHostEventEnvelope, Task>? registered = null;
        var host = new PluginUiStateHost(
            _ => { },
            _ => { },
            _ => Task.FromResult<object?>(Array.Empty<object>()),
            hostCapabilities: new HashSet<string>([PluginUiHostCapabilityNames.DaemonEventSubscribe], StringComparer.Ordinal),
            audit: audit.Add,
            eventBridge: new PluginUiEventBridge((_, handler) =>
            {
                registered = handler;
                return new DelegateDisposable(() => { });
            }),
            hostEvents: new HashSet<string>([PluginUiHostEventNames.InstanceLog], StringComparer.Ordinal));

        using (host.SubscribeEvent(PluginUiHostEventNames.InstanceLog, _ => Task.CompletedTask))
        {
            Assert.NotNull(registered);
            await registered!(new PluginUiHostEventEnvelope(PluginUiHostEventNames.InstanceLog, new { Line = "hello" }));
        }

        Assert.Collection(
            audit,
            entry =>
            {
                Assert.Equal("subscribeEvent", entry.Call);
                Assert.Equal(PluginUiHostCallOutcome.Ok, entry.Outcome);
            },
            entry =>
            {
                Assert.Equal("dispatchEvent", entry.Call);
                Assert.Equal(PluginUiHostCallOutcome.Ok, entry.Outcome);
            },
            entry =>
            {
                Assert.Equal("unsubscribeEvent", entry.Call);
                Assert.Equal(PluginUiHostCallOutcome.Ok, entry.Outcome);
            });
    }

    [Fact]
    public async Task HostNormalizesEventHandlerFailuresAndTimeouts()
    {
        List<PluginUiHostAuditEntry> failures = [];
        Func<PluginUiHostEventEnvelope, Task>? failingHandler = null;
        var failing = new PluginUiStateHost(
            _ => { },
            _ => { },
            _ => Task.FromResult<object?>(Array.Empty<object>()),
            hostCapabilities: new HashSet<string>([PluginUiHostCapabilityNames.DaemonEventSubscribe], StringComparer.Ordinal),
            audit: failures.Add,
            eventBridge: new PluginUiEventBridge((_, handler) =>
            {
                failingHandler = handler;
                return new DelegateDisposable(() => { });
            }),
            hostEvents: new HashSet<string>([PluginUiHostEventNames.InstanceLog], StringComparer.Ordinal));

        failing.SubscribeEvent(PluginUiHostEventNames.InstanceLog, _ => throw new InvalidOperationException("plugin failed"));
        var failed = await Assert.ThrowsAsync<PluginUiHostCallException>(() =>
            failingHandler!(new PluginUiHostEventEnvelope(PluginUiHostEventNames.InstanceLog, new { Line = "hello" })));
        Assert.Equal("host_callback_failed", failed.Code);
        Assert.Contains(failures, entry => entry.Call == "dispatchEvent" && entry.Outcome == PluginUiHostCallOutcome.Error && entry.Code == "host_callback_failed");

        List<PluginUiHostAuditEntry> timeouts = [];
        Func<PluginUiHostEventEnvelope, Task>? slowHandler = null;
        var slow = new PluginUiStateHost(
            _ => { },
            _ => { },
            _ => Task.FromResult<object?>(Array.Empty<object>()),
            hostCapabilities: new HashSet<string>([PluginUiHostCapabilityNames.DaemonEventSubscribe], StringComparer.Ordinal),
            audit: timeouts.Add,
            queryTimeout: TimeSpan.FromMilliseconds(5),
            eventBridge: new PluginUiEventBridge((_, handler) =>
            {
                slowHandler = handler;
                return new DelegateDisposable(() => { });
            }),
            hostEvents: new HashSet<string>([PluginUiHostEventNames.InstanceLog], StringComparer.Ordinal));

        slow.SubscribeEvent(PluginUiHostEventNames.InstanceLog, _ => Task.Delay(100));
        var timedOut = await Assert.ThrowsAsync<PluginUiHostCallException>(() =>
            slowHandler!(new PluginUiHostEventEnvelope(PluginUiHostEventNames.InstanceLog, new { Line = "hello" })));
        Assert.Equal("host_call_timeout", timedOut.Code);
        Assert.Contains(timeouts, entry => entry.Call == "dispatchEvent" && entry.Outcome == PluginUiHostCallOutcome.Timeout && entry.Code == "host_call_timeout");
    }

    [Fact]
    public void RendererEmitsButtonEventsThroughHostOnly()
    {
        RunOnSta(() =>
        {
            var parsed = PluginUiSchemaParser.Parse("""
            {
              "schema": "mcsl.ui.v1",
              "root": {
                "type": "Button",
                "props": { "Text": "Refresh", "OnClick": "refresh" }
              }
            }
            """);
            Assert.True(parsed.Ok);

            List<PluginUiEvent> events = [];
            var host = new RecordingPluginUiHost(events);
            var element = new PluginUiRenderer().RenderOnCurrentThread(parsed.Schema!, new Dictionary<string, object?>(), host);
            var button = Assert.IsType<Button>(element);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var uiEvent = Assert.Single(events);
            Assert.Equal("refresh", uiEvent.Handler);
            Assert.Equal("click", uiEvent.Kind);
        });
    }

    [Fact]
    public void RendererEmitsCommandEventsThroughHostOnly()
    {
        RunOnSta(() =>
        {
            var parsed = PluginUiSchemaParser.Parse("""
            {
              "schema": "mcsl.ui.v1",
              "root": {
                "type": "ToggleSwitch",
                "props": {
                  "IsOn": false,
                  "OnChanged": {
                    "Command": "setAutoRefresh",
                    "Params": {
                      "enabled": "{event.value}",
                      "mode": "{state.mode}"
                    }
                  }
                }
              }
            }
            """);
            Assert.True(parsed.Ok, string.Join(Environment.NewLine, parsed.Diagnostics.Select(diagnostic => diagnostic.Message)));

            List<PluginUiEvent> events = [];
            var host = new RecordingPluginUiHost(events);
            var element = new PluginUiRenderer().RenderOnCurrentThread(
                parsed.Schema!,
                new Dictionary<string, object?> { ["mode"] = "safe" },
                host);
            var checkBox = Assert.IsType<CheckBox>(element);

            checkBox.RaiseEvent(new RoutedEventArgs(CheckBox.CheckedEvent));

            var uiEvent = Assert.Single(events);
            Assert.Null(uiEvent.Handler);
            Assert.Equal("changed", uiEvent.Kind);
            Assert.Equal("setAutoRefresh", uiEvent.Command?.Command);
            var parameters = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(uiEvent.Command?.Params);
            Assert.Equal(true, parameters["enabled"]);
            Assert.Equal("safe", parameters["mode"]);
        });
    }

    [Fact]
    public void RuntimeHostForwardsRenderedCommandEventsToCallback()
    {
        RunOnSta(() =>
        {
            var parsed = PluginUiSchemaParser.Parse("""
            {
              "schema": "mcsl.ui.v1",
              "root": {
                "type": "Button",
                "props": {
                  "Text": "Refresh",
                  "OnClick": {
                    "Command": "refresh",
                    "Params": { "mode": "safe" }
                  }
                }
              }
            }
            """);
            Assert.True(parsed.Ok, string.Join(Environment.NewLine, parsed.Diagnostics.Select(diagnostic => diagnostic.Message)));

            List<PluginUiEvent> forwarded = [];
            var entry = new PluginClientExtensionCacheEntry(
                "community.example.status-panel",
                "1.0.0",
                new PluginMpxDeploymentPlan(
                    ["client"],
                    new PluginMpxClientDeployment(null, null, null, []),
                    null),
                parsed.Schema,
                null,
                [new PluginMpxCommand("refresh", null, null, "daemon")],
                [],
                [],
                new Dictionary<string, string>(),
                new Dictionary<string, string>());

            var host = new PluginClientExtensionRuntimeHost();
            var render = host.RenderAsync(
                new PluginClientExtensionRenderRequest(
                    entry,
                    new Dictionary<string, object?>(),
                    uiEvent =>
                    {
                        forwarded.Add(uiEvent);
                        return Task.CompletedTask;
                    }),
                System.Windows.Threading.Dispatcher.CurrentDispatcher).GetAwaiter().GetResult();
            Assert.True(render.Ok, string.Join(Environment.NewLine, render.Diagnostics.Select(diagnostic => diagnostic.Message)));

            var button = Assert.IsType<Button>(render.Root);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var uiEvent = Assert.Single(forwarded);
            Assert.Equal("refresh", uiEvent.Command?.Command);
            var parameters = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(uiEvent.Command?.Params);
            Assert.Equal("safe", parameters["mode"]);
        });
    }

    [Fact]
    public async Task GuardedRuntimeRejectsOversizedInitialStateBeforeExecution()
    {
        await using var runtime = new GuardedPluginScriptRuntime(new PluginScriptRuntimeOptions(MaxStateBytes: 8));
        var host = CreateStateCapableHost();
        var result = await runtime.ExecuteAsync(new PluginScriptExecutionRequest(
            "community.example.status-panel",
            "export function onLoad() {}",
            new Dictionary<string, object?> { ["message"] = new string('x', 256) },
            host));

        Assert.False(result.Success);
        Assert.Equal("state_too_large", result.Code);
    }

    [Fact]
    public async Task GuardedRuntimeRejectsNonSerializableInitialStateBeforeExecution()
    {
        await using var runtime = new GuardedPluginScriptRuntime();
        var host = CreateStateCapableHost();
        var result = await runtime.ExecuteAsync(new PluginScriptExecutionRequest(
            "community.example.status-panel",
            "export function onLoad() {}",
            new Dictionary<string, object?> { ["callback"] = new Action(() => { }) },
            host));

        Assert.False(result.Success);
        Assert.Equal("state_invalid", result.Code);
    }

    [Fact]
    public async Task GuardedRuntimeFailsClosedUntilScriptRuntimeIsExplicitlySupported()
    {
        await using var runtime = new GuardedPluginScriptRuntime();
        var host = CreateStateCapableHost();
        var result = await runtime.ExecuteAsync(new PluginScriptExecutionRequest(
            "community.example.status-panel",
            "export function onLoad() {}",
            new Dictionary<string, object?>(),
            host));

        Assert.False(result.Success);
        Assert.Equal("script_runtime_unavailable", result.Code);
    }

    private static PluginUiStateHost CreateStateCapableHost() =>
        new(
            _ => { },
            _ => { },
            _ => Task.FromResult<object?>(Array.Empty<object>()),
            hostCapabilities: new HashSet<string>([PluginUiHostCapabilityNames.UiState], StringComparer.Ordinal));

    private static PluginClientExtensionCacheEntry CreateCacheEntry(
        string id,
        IReadOnlyList<string>? eventPermissions = null) =>
        new(
            id,
            "1.0.0",
            new PluginMpxDeploymentPlan(
                ["client"],
                new PluginMpxClientDeployment(null, null, null, []),
                null),
            null,
            null,
            [],
            [],
            eventPermissions ?? [],
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        exception?.Throw();
    }

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private sealed class RecordingPluginUiHost(List<PluginUiEvent> events) : IPluginUiHost
    {
        public void Emit(PluginUiEvent uiEvent) => events.Add(uiEvent);
    }
}
