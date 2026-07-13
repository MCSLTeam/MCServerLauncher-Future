using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.DaemonClient.Protocol;
using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.DaemonClient.Connection.V2;

namespace MCServerLauncher.ProtocolTests.DaemonClient.V2;

public sealed class V2ClientProtocolTests
{
    [Fact]
    public void ClientInventoryIsAnExactIdentityPreservingProjectionOfTheAuthoritativeCatalog()
    {
        Assert.Equal(BuiltInProtocolDefinitions.Rpcs.Length, V2ClientProtocol.Rpcs.Length);
        Assert.Equal(BuiltInProtocolDefinitions.Events.Length, V2ClientProtocol.Events.Length);

        var authoritativeRpcs = BuiltInProtocolDefinitions.Rpcs.ToDictionary(item => item.Method);
        var authoritativeEvents = BuiltInProtocolDefinitions.Events.ToDictionary(item => item.Name);
        Assert.Equal(authoritativeRpcs.Keys.OrderBy(item => item.Value), V2ClientProtocol.Rpcs.Select(item => item.Method).OrderBy(item => item.Value));
        Assert.Equal(authoritativeEvents.Keys.OrderBy(item => item.Value), V2ClientProtocol.Events.Select(item => item.Name).OrderBy(item => item.Value));
        Assert.Equal(V2ClientProtocol.Rpcs.Length, V2ClientProtocol.Rpcs.Select(item => item.Method).Distinct().Count());
        Assert.Equal(V2ClientProtocol.Events.Length, V2ClientProtocol.Events.Select(item => item.Name).Distinct().Count());

        foreach (var client in V2ClientProtocol.Rpcs)
        {
            var authoritative = authoritativeRpcs[client.Method];
            Assert.Same(authoritative, client);
            Assert.Same(authoritative.RequestTypeInfo, client.RequestTypeInfo);
            Assert.Same(authoritative.ResultTypeInfo, client.ResultTypeInfo);
            Assert.Equal(authoritative.Permission, client.Permission);
            Assert.Equal(authoritative.AllowNotification, client.AllowNotification);
        }

        foreach (var client in V2ClientProtocol.Events)
        {
            var authoritative = authoritativeEvents[client.Name];
            Assert.Same(authoritative, client);
            Assert.Same(authoritative.DataTypeInfo, client.DataTypeInfo);
            Assert.Same(authoritative.MetaTypeInfo, client.MetaTypeInfo);
            Assert.Equal(authoritative.Permission, client.Permission);
            Assert.Equal(authoritative.DataPresence, client.DataPresence);
            Assert.Equal(authoritative.MetaPresence, client.MetaPresence);
        }
    }

    [Fact]
    public void EveryTypedAccessorMapsToItsExpectedWireIdentity()
    {
        RpcAccessorCase[] rpcCases =
        [
            Accessor(V2ClientProtocol.GetAuthPermissions, "mcsl.auth.permissions.get"),
            Accessor(V2ClientProtocol.PingDaemon, "mcsl.daemon.ping"),
            Accessor(V2ClientProtocol.CopyDirectory, "mcsl.directory.copy"),
            Accessor(V2ClientProtocol.CreateDirectory, "mcsl.directory.create"),
            Accessor(V2ClientProtocol.DeleteDirectory, "mcsl.directory.delete"),
            Accessor(V2ClientProtocol.GetDirectoryInfo, "mcsl.directory.info.get"),
            Accessor(V2ClientProtocol.MoveDirectory, "mcsl.directory.move"),
            Accessor(V2ClientProtocol.RenameDirectory, "mcsl.directory.rename"),
            Accessor(V2ClientProtocol.SubscribeEvent, "mcsl.event.subscribe"),
            Accessor(V2ClientProtocol.UnsubscribeEvent, "mcsl.event.unsubscribe"),
            Accessor(V2ClientProtocol.CopyFile, "mcsl.file.copy"),
            Accessor(V2ClientProtocol.DeleteFile, "mcsl.file.delete"),
            Accessor(V2ClientProtocol.CloseDownload, "mcsl.file.download.close"),
            Accessor(V2ClientProtocol.OpenDownload, "mcsl.file.download.open"),
            Accessor(V2ClientProtocol.ReadDownload, "mcsl.file.download.read"),
            Accessor(V2ClientProtocol.GetFileInfo, "mcsl.file.info.get"),
            Accessor(V2ClientProtocol.MoveFile, "mcsl.file.move"),
            Accessor(V2ClientProtocol.RenameFile, "mcsl.file.rename"),
            Accessor(V2ClientProtocol.CancelUpload, "mcsl.file.upload.cancel"),
            Accessor(V2ClientProtocol.CloseUpload, "mcsl.file.upload.close"),
            Accessor(V2ClientProtocol.OpenUpload, "mcsl.file.upload.open"),
            Accessor(V2ClientProtocol.GetInstanceCatalog, "mcsl.instance.catalog.get"),
            Accessor(V2ClientProtocol.SendInstanceCommand, "mcsl.instance.command.send"),
            Accessor(V2ClientProtocol.CreateInstance, "mcsl.instance.create"),
            Accessor(V2ClientProtocol.GetInstanceEventRules, "mcsl.instance.event-rules.get"),
            Accessor(V2ClientProtocol.UpdateInstanceEventRules, "mcsl.instance.event-rules.update"),
            Accessor(V2ClientProtocol.HaltInstance, "mcsl.instance.halt"),
            Accessor(V2ClientProtocol.GetInstanceLog, "mcsl.instance.log.get"),
            Accessor(V2ClientProtocol.RemoveInstance, "mcsl.instance.remove"),
            Accessor(V2ClientProtocol.GetInstanceReport, "mcsl.instance.report.get"),
            Accessor(V2ClientProtocol.ListInstanceReports, "mcsl.instance.report.list"),
            Accessor(V2ClientProtocol.GetInstanceSettings, "mcsl.instance.settings.get"),
            Accessor(V2ClientProtocol.UpdateInstanceSettings, "mcsl.instance.settings.update"),
            Accessor(V2ClientProtocol.StartInstance, "mcsl.instance.start"),
            Accessor(V2ClientProtocol.StopInstance, "mcsl.instance.stop"),
            Accessor(V2ClientProtocol.ListJavaRuntimes, "mcsl.java.list"),
            Accessor(V2ClientProtocol.GetSystemInfo, "mcsl.system.info.get"),
            Accessor(V2ClientProtocol.DiscoverRpc, "rpc.discover")
        ];
        EventAccessorCase[] eventCases =
        [
            EventAccessor(V2ClientProtocol.InstanceCatalogChanged, "mcsl.event.instance.catalog.changed"),
            EventAccessor(V2ClientProtocol.DaemonReport, "mcsl.event.daemon.report"),
            EventAccessor(V2ClientProtocol.InstanceLog, "mcsl.event.instance.log"),
            EventAccessor(V2ClientProtocol.Notification, "mcsl.event.notification")
        ];

        Assert.Equal(V2ClientProtocol.Rpcs.Length, rpcCases.Length);
        Assert.Equal(V2ClientProtocol.Events.Length, eventCases.Length);
        Assert.Equal(rpcCases.Length, rpcCases.Select(item => item.ExpectedMethod).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(eventCases.Length, eventCases.Select(item => item.ExpectedName).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            V2ClientProtocol.Rpcs.Select(item => item.Method.Value).Order(StringComparer.Ordinal),
            rpcCases.Select(item => item.ExpectedMethod).Order(StringComparer.Ordinal));
        Assert.Equal(
            V2ClientProtocol.Events.Select(item => item.Name.Value).Order(StringComparer.Ordinal),
            eventCases.Select(item => item.ExpectedName).Order(StringComparer.Ordinal));

        foreach (var @case in rpcCases)
        {
            Assert.Equal(@case.ExpectedMethod, @case.Descriptor.Method.Value);
            Assert.Contains(V2ClientProtocol.Rpcs, candidate => ReferenceEquals(@case.Descriptor, candidate));
        }

        foreach (var @case in eventCases)
        {
            Assert.Equal(@case.ExpectedName, @case.Descriptor.Name.Value);
            Assert.Contains(V2ClientProtocol.Events, candidate => ReferenceEquals(@case.Descriptor, candidate));
        }
    }

    [Fact]
    public async Task EveryRpcUsesItsTypedMappingForRequestAndResultGoldenRoundTrip()
    {
        IRpcGoldenCase[] cases =
        [
            Case(V2ClientProtocol.GetAuthPermissions), Case(V2ClientProtocol.PingDaemon), Case(V2ClientProtocol.CopyDirectory),
            Case(V2ClientProtocol.CreateDirectory), Case(V2ClientProtocol.DeleteDirectory), Case(V2ClientProtocol.GetDirectoryInfo),
            Case(V2ClientProtocol.MoveDirectory), Case(V2ClientProtocol.RenameDirectory), Case(V2ClientProtocol.SubscribeEvent),
            Case(V2ClientProtocol.UnsubscribeEvent), Case(V2ClientProtocol.CopyFile), Case(V2ClientProtocol.DeleteFile),
            Case(V2ClientProtocol.CloseDownload), Case(V2ClientProtocol.OpenDownload), Case(V2ClientProtocol.ReadDownload),
            Case(V2ClientProtocol.GetFileInfo), Case(V2ClientProtocol.MoveFile), Case(V2ClientProtocol.RenameFile),
            Case(V2ClientProtocol.CancelUpload), Case(V2ClientProtocol.CloseUpload), Case(V2ClientProtocol.OpenUpload),
            Case(V2ClientProtocol.GetInstanceCatalog), Case(V2ClientProtocol.SendInstanceCommand), Case(V2ClientProtocol.CreateInstance),
            Case(V2ClientProtocol.GetInstanceEventRules), Case(V2ClientProtocol.UpdateInstanceEventRules),
            Case(V2ClientProtocol.HaltInstance), Case(V2ClientProtocol.GetInstanceLog), Case(V2ClientProtocol.RemoveInstance),
            Case(V2ClientProtocol.GetInstanceReport), Case(V2ClientProtocol.ListInstanceReports),
            Case(V2ClientProtocol.GetInstanceSettings), Case(V2ClientProtocol.UpdateInstanceSettings),
            Case(V2ClientProtocol.StartInstance), Case(V2ClientProtocol.StopInstance), Case(V2ClientProtocol.ListJavaRuntimes),
            Case(V2ClientProtocol.GetSystemInfo), Case(V2ClientProtocol.DiscoverRpc)
        ];

        Assert.Equal(V2ClientProtocol.Rpcs.Length, cases.Length);
        foreach (var @case in cases)
        {
            await @case.VerifyAsync();
        }
    }

    [Fact]
    public void EveryEventUsesItsTypedMappingForGoldenFieldPresenceAndSerialization()
    {
        IEventGoldenCase[] cases =
        [
            EventCase(V2ClientProtocol.InstanceCatalogChanged), EventCase(V2ClientProtocol.DaemonReport),
            EventCase(V2ClientProtocol.InstanceLog), EventCase(V2ClientProtocol.Notification)
        ];

        Assert.Equal(V2ClientProtocol.Events.Length, cases.Length);
        foreach (var @case in cases)
        {
            @case.Verify();
        }
    }

    private static IRpcGoldenCase Case<TRequest, TResult>(RpcDescriptor<TRequest, TResult> descriptor)
        where TResult : notnull => new RpcGoldenCase<TRequest, TResult>(descriptor);

    private static RpcAccessorCase Accessor<TRequest, TResult>(
        RpcDescriptor<TRequest, TResult> descriptor,
        string expectedMethod) =>
        new(descriptor, expectedMethod);

    private static IEventGoldenCase EventCase<TData, TMeta>(EventDescriptor<TData, TMeta> descriptor) =>
        new EventGoldenCase<TData, TMeta>(descriptor);

    private static EventAccessorCase EventAccessor<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        string expectedName) =>
        new(descriptor, expectedName);

    private sealed record RpcAccessorCase(RpcDescriptor Descriptor, string ExpectedMethod);

    private sealed record EventAccessorCase(EventDescriptor Descriptor, string ExpectedName);

    private interface IRpcGoldenCase
    {
        Task VerifyAsync();
    }

    private sealed class RpcGoldenCase<TRequest, TResult>(RpcDescriptor<TRequest, TResult> descriptor) : IRpcGoldenCase
        where TResult : notnull
    {
        public async Task VerifyAsync()
        {
            const string requestId = "catalog-golden";
            var request = JsonSerializer.Deserialize(GoldenJson.For(typeof(TRequest)), descriptor.RequestTypeInfo)!;
            var transport = new GoldenTransport();
            var core = new V2ClientConnectionCore(
                transport,
                TimeProvider.System,
                TimeSpan.FromMinutes(1),
                () => JsonRpcRequestId.FromString(requestId));

            var pending = core.InvokeAsync(descriptor, request);
            using var sent = JsonDocument.Parse(transport.Text);
            Assert.Equal("2.0", sent.RootElement.GetProperty("jsonrpc").GetString());
            Assert.Equal(descriptor.Method.Value, sent.RootElement.GetProperty("method").GetString());
            Assert.Equal(requestId, sent.RootElement.GetProperty("id").GetString());
            if (typeof(TRequest) == typeof(EmptyRequest))
            {
                Assert.False(sent.RootElement.TryGetProperty("params", out _));
            }
            else
            {
                var parameters = sent.RootElement.GetProperty("params");
                Assert.NotNull(parameters.Deserialize(descriptor.RequestTypeInfo));
                using var expected = JsonDocument.Parse(GoldenJson.For(typeof(TRequest)));
                Assert.True(JsonElement.DeepEquals(expected.RootElement, parameters), descriptor.Method.Value);
            }

            var resultJson = GoldenJson.For(typeof(TResult));
            Assert.NotNull(JsonSerializer.Deserialize(resultJson, descriptor.ResultTypeInfo));
            core.RouteText(System.Text.Encoding.UTF8.GetBytes($"{{\"jsonrpc\":\"2.0\",\"id\":\"{requestId}\",\"result\":{resultJson}}}"));
            var result = await pending;
            Assert.True(result.IsOk(out var actual), descriptor.Method.Value);
            using var expectedResult = JsonDocument.Parse(resultJson);
            var actualResult = JsonSerializer.SerializeToElement(actual, descriptor.ResultTypeInfo);
            Assert.True(
                JsonElement.DeepEquals(expectedResult.RootElement, actualResult),
                $"{descriptor.Method.Value}{Environment.NewLine}Expected: {expectedResult.RootElement}{Environment.NewLine}Actual: {actualResult}");
            Assert.Equal(0, core.PendingCount);

            // download.read covers its JSON metadata result here; binary attachment completion belongs to the later coordinator slice.
        }
    }

    private interface IEventGoldenCase
    {
        void Verify();
    }

    private sealed class EventGoldenCase<TData, TMeta>(EventDescriptor<TData, TMeta> descriptor) : IEventGoldenCase
    {
        public void Verify()
        {
            var dataJson = GoldenJson.For(typeof(TData));
            var data = JsonSerializer.Deserialize(dataJson, descriptor.DataTypeInfo)!;
            using var expectedData = JsonDocument.Parse(GoldenJson.SerializedFor(typeof(TData)));
            Assert.True(JsonElement.DeepEquals(expectedData.RootElement, JsonSerializer.SerializeToElement(data, descriptor.DataTypeInfo)));
            Assert.Equal(OpenRpcEventFieldPresence.Required, descriptor.DataPresence);
            if (descriptor.MetaPresence == OpenRpcEventFieldPresence.Omitted)
            {
                Assert.Null(descriptor.MetaTypeInfo);
            }
            else
            {
                var metaJson = GoldenJson.For(typeof(TMeta));
                var meta = JsonSerializer.Deserialize(metaJson, descriptor.MetaTypeInfo!)!;
                using var expectedMeta = JsonDocument.Parse(metaJson);
                Assert.True(JsonElement.DeepEquals(expectedMeta.RootElement, JsonSerializer.SerializeToElement(meta, descriptor.MetaTypeInfo!)));
            }

            // Notification materialization and delivery are intentionally outside this catalog-mapping slice.
        }
    }

    private static class GoldenJson
    {
        private const string Id = "11111111-1111-1111-1111-111111111111";
        private const string RuleId = "22222222-2222-2222-2222-222222222222";
        private const string Config = "{\"instance_id\":\"" + Id + "\",\"name\":\"demo\",\"target\":\"server.jar\",\"instance_type\":\"universal\",\"target_type\":\"jar\",\"version\":\"1\",\"input_encoding\":\"utf-8\",\"output_encoding\":\"utf-8\",\"java_path\":\"java\",\"arguments\":[\"-Xmx2G\"],\"environment_variables\":{\"ENV\":\"value\"},\"event_rules\":{\"enabled\":true}}";
        private const string FileMeta = "{\"creation_time\":\"2026-01-01T00:00:00+00:00\",\"hidden\":true,\"last_access_time\":\"2026-01-02T00:00:00+00:00\",\"last_write_time\":\"2026-01-03T00:00:00+00:00\",\"read_only\":true,\"size\":7}";
        private const string DirectoryMeta = "{\"creation_time\":\"2026-01-04T00:00:00+00:00\",\"hidden\":true,\"last_access_time\":\"2026-01-05T00:00:00+00:00\",\"last_write_time\":\"2026-01-06T00:00:00+00:00\"}";
        private const string Report = "{\"status\":\"running\",\"config\":" + Config + ",\"properties\":{\"motd\":\"hello\"},\"players\":[{\"name\":\"Alex\",\"uuid\":\"33333333-3333-3333-3333-333333333333\"}],\"performance_counter\":{\"cpu\":12.5,\"memory_bytes\":1024},\"process_id\":1234}";
        private const string System = "{\"os\":{\"name\":\"Windows\",\"architecture\":\"x64\"},\"cpu\":{\"vendor\":\"vendor\",\"name\":\"cpu\",\"count\":16,\"usage\":5.5,\"core_count\":8,\"thread_count\":16},\"mem\":{\"total_kilobytes\":32768,\"free_kilobytes\":16384},\"drive\":{\"drive_format\":\"NTFS\",\"total_bytes\":1024,\"free_bytes\":512,\"name\":\"C\"},\"drives\":[{\"drive_format\":\"EXT4\",\"total_bytes\":2048,\"free_bytes\":1024,\"name\":\"D\"}],\"daemon_version\":\"2.0.0\"}";

        internal static string For(Type type)
        {
            if (type == typeof(EmptyRequest) || type == typeof(UnitResult)) return "{}";
            if (type == typeof(PathRequest) || type == typeof(DownloadOpenRequest)) return "{\"path\":\"world\"}";
            if (type == typeof(PathTransferRequest)) return "{\"source_path\":\"a\",\"destination_path\":\"b\"}";
            if (type == typeof(PathRenameRequest)) return "{\"path\":\"a\",\"new_name\":\"b\"}";
            if (type == typeof(DeleteDirectoryRequest)) return "{\"path\":\"a\",\"recursive\":false}";
            if (type == typeof(EventSubscriptionRequest)) return "{\"event\":\"mcsl.event.daemon.report\"}";
            if (type == typeof(FileSessionReference)) return "{\"session_id\":\"" + Id + "\"}";
            if (type == typeof(DownloadChunkRequest)) return "{\"session_id\":\"" + Id + "\",\"offset\":0,\"maximum_length\":1}";
            if (type == typeof(UploadOpenRequest)) return "{\"path\":\"world\",\"length\":0,\"sha256\":\"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\"}";
            if (type == typeof(InstanceReference) || type == typeof(InstanceLogQuery) || type == typeof(EventRuleQuery)) return "{\"instance_id\":\"" + Id + "\"}";
            if (type == typeof(InstanceCommandRequest)) return "{\"instance_id\":\"" + Id + "\",\"command\":\"say hi\"}";
            if (type == typeof(CreateInstanceRequest)) return "{\"setting\":{\"configuration\":" + Config + ",\"source\":\"server.jar\",\"source_type\":\"core\",\"mirror\":\"none\",\"use_post_process\":false}}";
            if (type == typeof(EventRuleUpdateRequest)) return "{\"instance_id\":\"" + Id + "\",\"rules\":{}}";
            if (type == typeof(UpdateInstanceSettingsRequest)) return "{\"instance_id\":\"" + Id + "\",\"name\":\"demo\",\"instance_type\":\"universal\",\"java_path\":null,\"arguments\":[],\"version\":null,\"replacement_core\":null,\"force_rerun_installer\":false}";

            if (type == typeof(PermissionsResult)) return "{\"permissions\":[\"mcsl.daemon.read\"]}";
            if (type == typeof(PingResult)) return "{\"time\":42}";
            if (type == typeof(DirectoryDetails)) return "{\"parent\":\"instances\",\"files\":[{\"name\":\"server.jar\",\"meta\":" + FileMeta + "}],\"directories\":[{\"name\":\"plugins\",\"meta\":" + DirectoryMeta + "}]}";
            if (type == typeof(FileDetails)) return "{\"meta\":" + FileMeta + "}";
            if (type == typeof(DownloadSession)) return "{\"session_id\":\"" + Id + "\",\"length\":7,\"sha256\":\"hash\",\"max_chunk_size\":4,\"expires_at\":\"2026-01-01T00:00:00+00:00\"}";
            if (type == typeof(UploadSession)) return "{\"session_id\":\"" + Id + "\",\"max_chunk_size\":4,\"expires_at\":\"2026-01-01T00:00:00+00:00\"}";
            if (type == typeof(DownloadReadResult)) return "{\"session_id\":\"" + Id + "\",\"offset\":3,\"length\":4,\"is_final\":true}";
            if (type == typeof(InstanceCatalogResult)) return "{\"version\":7,\"items\":[{\"instance_id\":\"" + Id + "\",\"name\":\"demo\",\"instance_type\":\"universal\",\"version\":\"1\",\"status\":\"running\"}]}";
            if (type == typeof(CreateInstanceResult)) return "{\"config\":" + Config + "}";
            if (type == typeof(EventRuleSet)) return "{\"instance_id\":\"" + Id + "\",\"rules\":{\"enabled\":true}}";
            if (type == typeof(InstanceLogResult)) return "{\"logs\":[\"ready\"]}";
            if (type == typeof(InstanceReport)) return Report;
            if (type == typeof(InstanceReportList)) return "{\"reports\":{\"" + Id + "\":" + Report + "}}";
            if (type == typeof(InstanceSettingsResult)) return "{\"config\":" + Config + ",\"working_directory\":\"world\",\"current_target_exists\":true,\"can_edit\":false,\"edit_blocked_reason\":\"running\",\"install_metadata\":{\"installer_kind\":\"forge\",\"installer_source_path\":\"forge.jar\",\"generated_paths\":[\"libraries\"],\"resolved_launch_target\":\"run.jar\",\"installed_at\":\"2026-01-01T00:00:00+00:00\"}}";
            if (type == typeof(UpdateInstanceSettingsResult)) return "{\"config\":" + Config + ",\"requires_restart\":true,\"reinstalled\":true,\"deleted_generated_paths\":[\"old.jar\"],\"preserved_original_paths\":[\"backup.jar\"]}";
            if (type == typeof(JavaRuntimeList)) return "{\"items\":[{\"path\":\"C:/Java/bin/java.exe\",\"version\":\"21.0.7\",\"architecture\":\"x64\"}]}";
            if (type == typeof(SystemInfo)) return System;
            if (type == typeof(OpenRpcDocument)) return "{\"openrpc\":\"1.3.2\",\"info\":{\"title\":\"daemon\",\"version\":\"2\"},\"methods\":[],\"x-mcsl-events\":[],\"components\":{\"schemas\":{}}}";

            if (type == typeof(InstanceCatalogChangedEventData)) return "{\"version\":1,\"operation\":\"remove\",\"instance_id\":\"" + Id + "\",\"snapshot\":null}";
            if (type == typeof(DaemonReportEventData)) return "{\"system_info\":" + System + ",\"start_timestamp\":1}";
            if (type == typeof(InstanceLogEventData)) return "{\"log\":\"started\"}";
            if (type == typeof(InstanceLogEventMeta)) return "{\"instance_id\":\"" + Id + "\"}";
            if (type == typeof(NotificationEventData)) return "{\"title\":\"title\",\"message\":\"message\",\"severity\":\"info\"}";
            if (type == typeof(NotificationEventMeta)) return "{\"source_instance_id\":\"" + Id + "\",\"rule_id\":\"" + RuleId + "\"}";

            throw new InvalidOperationException($"No handwritten golden JSON exists for {type}.");
        }

        internal static string SerializedFor(Type type) =>
            type == typeof(InstanceCatalogChangedEventData)
                ? "{\"version\":1,\"operation\":\"remove\",\"instance_id\":\"" + Id + "\"}"
                : For(type);
    }

    private sealed class GoldenTransport : IV2ClientWireTransport
    {
        internal string Text { get; private set; } = string.Empty;

        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken)
        {
            Text = System.Text.Encoding.UTF8.GetString(utf8Json.AsSpan());
            return ValueTask.CompletedTask;
        }

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
