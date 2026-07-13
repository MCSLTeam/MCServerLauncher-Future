using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Protocol;
using InstanceStatus = MCServerLauncher.Common.ProtoType.Instance.InstanceStatus;
using InstanceType = MCServerLauncher.Common.ProtoType.Instance.InstanceType;

namespace MCServerLauncher.Daemon.ApiTests;

public sealed class BuiltInProtocolDefinitionTests
{
    [Fact]
    public void NamedTypedDescriptorsAreTheExactFrozenCatalogInstances()
    {
        RpcDescriptor[] namedRpcs =
        [
            BuiltInProtocolDefinitions.GetAuthPermissions, BuiltInProtocolDefinitions.PingDaemon,
            BuiltInProtocolDefinitions.CopyDirectory, BuiltInProtocolDefinitions.CreateDirectory,
            BuiltInProtocolDefinitions.DeleteDirectory, BuiltInProtocolDefinitions.GetDirectoryInfo,
            BuiltInProtocolDefinitions.MoveDirectory, BuiltInProtocolDefinitions.RenameDirectory,
            BuiltInProtocolDefinitions.SubscribeEvent, BuiltInProtocolDefinitions.UnsubscribeEvent,
            BuiltInProtocolDefinitions.CopyFile, BuiltInProtocolDefinitions.DeleteFile,
            BuiltInProtocolDefinitions.CloseDownload, BuiltInProtocolDefinitions.OpenDownload,
            BuiltInProtocolDefinitions.ReadDownload, BuiltInProtocolDefinitions.GetFileInfo,
            BuiltInProtocolDefinitions.MoveFile, BuiltInProtocolDefinitions.RenameFile,
            BuiltInProtocolDefinitions.CancelUpload, BuiltInProtocolDefinitions.CloseUpload,
            BuiltInProtocolDefinitions.OpenUpload, BuiltInProtocolDefinitions.GetInstanceCatalog,
            BuiltInProtocolDefinitions.SendInstanceCommand, BuiltInProtocolDefinitions.CreateInstance,
            BuiltInProtocolDefinitions.GetInstanceEventRules, BuiltInProtocolDefinitions.UpdateInstanceEventRules,
            BuiltInProtocolDefinitions.HaltInstance, BuiltInProtocolDefinitions.GetInstanceLog,
            BuiltInProtocolDefinitions.RemoveInstance, BuiltInProtocolDefinitions.GetInstanceReport,
            BuiltInProtocolDefinitions.ListInstanceReports, BuiltInProtocolDefinitions.GetInstanceSettings,
            BuiltInProtocolDefinitions.UpdateInstanceSettings, BuiltInProtocolDefinitions.StartInstance,
            BuiltInProtocolDefinitions.StopInstance, BuiltInProtocolDefinitions.ListJavaRuntimes,
            BuiltInProtocolDefinitions.GetSystemInfo, BuiltInProtocolDefinitions.DiscoverRpc
        ];
        EventDescriptor[] namedEvents =
        [
            BuiltInProtocolDefinitions.InstanceCatalogChanged, BuiltInProtocolDefinitions.DaemonReport,
            BuiltInProtocolDefinitions.InstanceLog, BuiltInProtocolDefinitions.Notification
        ];

        Assert.Equal(BuiltInProtocolDefinitions.Rpcs.Length, namedRpcs.Length);
        Assert.Equal(BuiltInProtocolDefinitions.Events.Length, namedEvents.Length);
        Assert.All(namedRpcs, descriptor => Assert.Contains(BuiltInProtocolDefinitions.Rpcs, candidate => ReferenceEquals(candidate, descriptor)));
        Assert.All(namedEvents, descriptor => Assert.Contains(BuiltInProtocolDefinitions.Events, candidate => ReferenceEquals(candidate, descriptor)));
        Assert.Equal(namedRpcs.Length, namedRpcs.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(namedEvents.Length, namedEvents.Distinct(ReferenceEqualityComparer.Instance).Count());
    }

    [Fact]
    public void BuiltInDefinitionsContainTheExactFrozenRpcAndEventNames()
    {
        var expectedRpcs = new[]
        {
            "mcsl.auth.permissions.get", "mcsl.daemon.ping", "mcsl.directory.copy", "mcsl.directory.create", "mcsl.directory.delete",
            "mcsl.directory.info.get", "mcsl.directory.move", "mcsl.directory.rename", "mcsl.event.subscribe",
            "mcsl.event.unsubscribe", "mcsl.file.copy", "mcsl.file.delete", "mcsl.file.download.close",
            "mcsl.file.download.open", "mcsl.file.download.read", "mcsl.file.info.get", "mcsl.file.move",
            "mcsl.file.rename", "mcsl.file.upload.cancel", "mcsl.file.upload.close", "mcsl.file.upload.open",
            "mcsl.instance.catalog.get", "mcsl.instance.command.send", "mcsl.instance.create", "mcsl.instance.event-rules.get",
            "mcsl.instance.event-rules.update", "mcsl.instance.halt", "mcsl.instance.log.get", "mcsl.instance.remove",
            "mcsl.instance.report.get", "mcsl.instance.report.list", "mcsl.instance.settings.get", "mcsl.instance.settings.update",
            "mcsl.instance.start", "mcsl.instance.stop", "mcsl.java.list", "mcsl.system.info.get", "rpc.discover"
        };
        var expectedEvents = new[]
        {
            "mcsl.event.daemon.report", "mcsl.event.instance.catalog.changed", "mcsl.event.instance.log", "mcsl.event.notification"
        };

        Assert.Equal(expectedRpcs.Length, BuiltInProtocolDefinitions.Rpcs.Length);
        Assert.Equal(expectedRpcs, BuiltInProtocolDefinitions.Rpcs.Select(descriptor => descriptor.Method.Value));
        Assert.Equal(expectedEvents, BuiltInProtocolDefinitions.Events.Select(descriptor => descriptor.Name.Value));
        Assert.Equal(BuiltInProtocolDefinitions.Rpcs.Length, BuiltInProtocolDefinitions.Rpcs.Select(descriptor => descriptor.Method).Distinct().Count());
        Assert.Equal(BuiltInProtocolDefinitions.Events.Length, BuiltInProtocolDefinitions.Events.Select(descriptor => descriptor.Name).Distinct().Count());
    }

    [Fact]
    public void BuiltInRpcsHaveTheFrozenPermissionAndClrTypeMappings()
    {
        var expected = new Dictionary<string, (string Permission, Type Request, Type Result)>(StringComparer.Ordinal)
        {
            ["mcsl.auth.permissions.get"] = ("*", typeof(EmptyRequest), typeof(PermissionsResult)),
            ["mcsl.daemon.ping"] = ("*", typeof(EmptyRequest), typeof(PingResult)),
            ["mcsl.directory.copy"] = ("mcsl.daemon.file.copy.directory", typeof(PathTransferRequest), typeof(UnitResult)),
            ["mcsl.directory.create"] = ("mcsl.daemon.file.create.directory", typeof(PathRequest), typeof(UnitResult)),
            ["mcsl.directory.delete"] = ("mcsl.daemon.file.delete.directory", typeof(DeleteDirectoryRequest), typeof(UnitResult)),
            ["mcsl.directory.info.get"] = ("mcsl.daemon.file.info.directory", typeof(PathRequest), typeof(DirectoryDetails)),
            ["mcsl.directory.move"] = ("mcsl.daemon.file.move.directory", typeof(PathTransferRequest), typeof(UnitResult)),
            ["mcsl.directory.rename"] = ("mcsl.daemon.file.rename.directory", typeof(PathRenameRequest), typeof(UnitResult)),
            ["mcsl.event.subscribe"] = ("*", typeof(EventSubscriptionRequest), typeof(UnitResult)),
            ["mcsl.event.unsubscribe"] = ("*", typeof(EventSubscriptionRequest), typeof(UnitResult)),
            ["mcsl.file.copy"] = ("mcsl.daemon.file.copy.file", typeof(PathTransferRequest), typeof(UnitResult)),
            ["mcsl.file.delete"] = ("mcsl.daemon.file.delete.file", typeof(PathRequest), typeof(UnitResult)),
            ["mcsl.file.download.close"] = ("mcsl.daemon.file.download", typeof(FileSessionReference), typeof(UnitResult)),
            ["mcsl.file.download.open"] = ("mcsl.daemon.file.download", typeof(DownloadOpenRequest), typeof(DownloadSession)),
            ["mcsl.file.download.read"] = ("mcsl.daemon.file.download", typeof(DownloadChunkRequest), typeof(DownloadReadResult)),
            ["mcsl.file.info.get"] = ("mcsl.daemon.file.info.file", typeof(PathRequest), typeof(FileDetails)),
            ["mcsl.file.move"] = ("mcsl.daemon.file.move.file", typeof(PathTransferRequest), typeof(UnitResult)),
            ["mcsl.file.rename"] = ("mcsl.daemon.file.rename.file", typeof(PathRenameRequest), typeof(UnitResult)),
            ["mcsl.file.upload.cancel"] = ("mcsl.daemon.file.upload", typeof(FileSessionReference), typeof(UnitResult)),
            ["mcsl.file.upload.close"] = ("mcsl.daemon.file.upload", typeof(FileSessionReference), typeof(UnitResult)),
            ["mcsl.file.upload.open"] = ("mcsl.daemon.file.upload", typeof(UploadOpenRequest), typeof(UploadSession)),
            ["mcsl.instance.catalog.get"] = ("*", typeof(EmptyRequest), typeof(InstanceCatalogResult)),
            ["mcsl.instance.command.send"] = ("*", typeof(InstanceCommandRequest), typeof(UnitResult)),
            ["mcsl.instance.create"] = ("*", typeof(CreateInstanceRequest), typeof(CreateInstanceResult)),
            ["mcsl.instance.event-rules.get"] = ("*", typeof(EventRuleQuery), typeof(EventRuleSet)),
            ["mcsl.instance.event-rules.update"] = ("*", typeof(EventRuleUpdateRequest), typeof(UnitResult)),
            ["mcsl.instance.halt"] = ("*", typeof(InstanceReference), typeof(UnitResult)),
            ["mcsl.instance.log.get"] = ("*", typeof(InstanceLogQuery), typeof(InstanceLogResult)),
            ["mcsl.instance.remove"] = ("*", typeof(InstanceReference), typeof(UnitResult)),
            ["mcsl.instance.report.get"] = ("*", typeof(InstanceReference), typeof(InstanceReport)),
            ["mcsl.instance.report.list"] = ("*", typeof(EmptyRequest), typeof(InstanceReportList)),
            ["mcsl.instance.settings.get"] = ("*", typeof(InstanceReference), typeof(InstanceSettingsResult)),
            ["mcsl.instance.settings.update"] = ("*", typeof(UpdateInstanceSettingsRequest), typeof(UpdateInstanceSettingsResult)),
            ["mcsl.instance.start"] = ("*", typeof(InstanceReference), typeof(UnitResult)),
            ["mcsl.instance.stop"] = ("*", typeof(InstanceReference), typeof(UnitResult)),
            ["mcsl.java.list"] = ("mcsl.daemon.java_list", typeof(EmptyRequest), typeof(JavaRuntimeList)),
            ["mcsl.system.info.get"] = ("*", typeof(EmptyRequest), typeof(SystemInfo)),
            ["rpc.discover"] = ("*", typeof(EmptyRequest), typeof(OpenRpcDocument))
        };

        foreach (var descriptor in BuiltInProtocolDefinitions.Rpcs)
        {
            var mapping = expected[descriptor.Method.Value];
            Assert.Equal(mapping.Permission, descriptor.Permission.Value);
            Assert.Equal(mapping.Request, descriptor.RequestTypeInfo.Type);
            Assert.Equal(mapping.Result, descriptor.ResultTypeInfo.Type);
            Assert.False(descriptor.AllowNotification);
            Assert.NotNull(descriptor.Documentation);
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Documentation!.Category));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Documentation.RequestSchemaId));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Documentation.ResultSchemaId));
            Assert.Equal(descriptor.Method.Value, new RpcMethod(descriptor.Method.Value).Value);
            Assert.Equal(descriptor.Permission.Value, new PermissionName(descriptor.Permission.Value).Value);
        }

        Assert.Equal(expected.Count, BuiltInProtocolDefinitions.Rpcs.Length);
    }

    [Fact]
    public void BuiltInEventsHaveTheFrozenPermissionTypeAndFieldPresenceMappings()
    {
        var expected = new Dictionary<string, (Type Data, Type? Meta, OpenRpcEventFieldPresence MetaPresence)>(StringComparer.Ordinal)
        {
            ["mcsl.event.daemon.report"] = (typeof(DaemonReportEventData), null, OpenRpcEventFieldPresence.Omitted),
            ["mcsl.event.instance.catalog.changed"] = (typeof(InstanceCatalogChangedEventData), null, OpenRpcEventFieldPresence.Omitted),
            ["mcsl.event.instance.log"] = (typeof(InstanceLogEventData), typeof(InstanceLogEventMeta), OpenRpcEventFieldPresence.Required),
            ["mcsl.event.notification"] = (typeof(NotificationEventData), typeof(NotificationEventMeta), OpenRpcEventFieldPresence.Required)
        };

        foreach (var descriptor in BuiltInProtocolDefinitions.Events)
        {
            var mapping = expected[descriptor.Name.Value];
            Assert.Equal("*", descriptor.Permission.Value);
            Assert.Equal(mapping.Data, descriptor.DataTypeInfo.Type);
            Assert.Equal(mapping.Meta, descriptor.MetaTypeInfo?.Type);
            Assert.Equal(OpenRpcEventFieldPresence.Required, descriptor.DataPresence);
            Assert.Equal(mapping.MetaPresence, descriptor.MetaPresence);
            Assert.NotNull(descriptor.Documentation);
            Assert.Equal(mapping.Meta is null, descriptor.Documentation!.MetaSchemaId is null);
            Assert.Equal(descriptor.Name.Value, new EventName(descriptor.Name.Value).Value);
        }
    }

    [Fact]
    public void OpenRpcDocumentIsDeterministicUsesInlineSchemasAndPreservesFieldSemantics()
    {
        var first = BuiltInProtocolDefinitions.CreateDocument("2.0.0");
        var second = BuiltInProtocolDefinitions.CreateDocument("2.0.0");
        var context = BuiltInProtocolJsonContext.Default.OpenRpcDocument;
        var firstJson = JsonSerializer.Serialize(first, context);
        var secondJson = JsonSerializer.Serialize(second, context);

        Assert.Equal(firstJson, secondJson);
        Assert.Equal(BuiltInProtocolDefinitions.Rpcs.Select(descriptor => descriptor.Method.Value), first.Methods.Select(method => method.Name));
        Assert.Equal(BuiltInProtocolDefinitions.Events.Select(descriptor => descriptor.Name.Value), first.Events.Select(@event => @event.Name));
        Assert.All(first.Methods, method =>
        {
            Assert.Equal("result", method.Result.Name);
            Assert.False(method.AllowNotification);
            Assert.True(method.Result.Schema.TryGetProperty("$id", out _));
            AssertSchemaRejectsNull(method.Result.Schema);
        });

        var settings = first.Methods.Single(method => method.Name == "mcsl.instance.settings.update");
        Assert.Equal(
            ["arguments", "force_rerun_installer", "instance_id", "instance_type", "java_path", "name", "replacement_core", "version"],
            settings.Params.Select(parameter => parameter.Name));
        Assert.Contains(settings.Params, parameter => parameter.Name == "instance_id" && parameter.Required);
        Assert.Contains(settings.Params, parameter => parameter.Name == "force_rerun_installer" && parameter.Required);

        var ping = first.Methods.Single(method => method.Name == "mcsl.daemon.ping");
        Assert.Empty(ping.Params);
        var unit = first.Methods.Single(method => method.Name == "mcsl.instance.start").Result.Schema;
        Assert.Equal("mcsl.schema.mcsl.instance.start.result", unit.GetProperty("$id").GetString());
        Assert.Equal("object", unit.GetProperty("type").GetString());
        Assert.Empty(unit.GetProperty("properties").EnumerateObject());
        Assert.False(unit.GetProperty("additionalProperties").GetBoolean());

        var log = first.Events.Single(@event => @event.Name == "mcsl.event.instance.log");
        Assert.Equal(OpenRpcEventFieldPresence.Required, log.Data.Presence);
        Assert.Equal(OpenRpcEventFieldPresence.Required, log.Meta.Presence);
        Assert.True(log.Data.Schema!.Value.GetProperty("properties").TryGetProperty("log", out _));
        Assert.True(log.Meta.Schema!.Value.GetProperty("properties").TryGetProperty("instance_id", out _));
        AssertSchemaRejectsNull(log.Data.Schema.Value);
        AssertSchemaRejectsNull(log.Meta.Schema.Value);
        var report = first.Events.Single(@event => @event.Name == "mcsl.event.daemon.report");
        Assert.Equal(OpenRpcEventFieldPresence.Omitted, report.Meta.Presence);
        Assert.Null(report.Meta.Schema);

        var catalogChangeSchema = first.Events
            .Single(@event => @event.Name == "mcsl.event.instance.catalog.changed")
            .Data.Schema!.Value;
        var catalogChangeRequired = catalogChangeSchema.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
        Assert.Equal(["version", "operation", "instance_id"], catalogChangeRequired);
        Assert.Equal("object", catalogChangeSchema.GetProperty("properties").GetProperty("snapshot").GetProperty("type").GetString());

        var catalogChangeConditions = catalogChangeSchema.GetProperty("allOf").EnumerateArray().ToArray();
        Assert.Equal(2, catalogChangeConditions.Length);
        AssertCatalogChangeCondition(catalogChangeConditions[0], "upsert", snapshotRequired: true);
        AssertCatalogChangeCondition(catalogChangeConditions[1], "remove", snapshotRequired: false);

        var snapshot = new InstanceCatalogItem(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "first",
            InstanceType.MCJava,
            "1.21.5",
            InstanceStatus.Running);
        var upsertJson = JsonSerializer.Serialize(
            new InstanceCatalogChangedEventData(1, InstanceCatalogChangeOperation.Upsert, snapshot.InstanceId, snapshot),
            BuiltInProtocolJsonContext.Default.InstanceCatalogChangedEventData);
        var removeJson = JsonSerializer.Serialize(
            new InstanceCatalogChangedEventData(2, InstanceCatalogChangeOperation.Remove, snapshot.InstanceId, null),
            BuiltInProtocolJsonContext.Default.InstanceCatalogChangedEventData);
        Assert.Contains("\"operation\":\"upsert\"", upsertJson, StringComparison.Ordinal);
        Assert.Contains("\"snapshot\":", upsertJson, StringComparison.Ordinal);
        Assert.Contains("\"operation\":\"remove\"", removeJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"snapshot\":", removeJson, StringComparison.Ordinal);

        var subscription = first.Methods.Single(method => method.Name == "mcsl.event.subscribe");
        Assert.Equal(["event", "meta"], subscription.Params.Select(parameter => parameter.Name));
        var eventParameter = subscription.Params.Single(parameter => parameter.Name == "event");
        Assert.True(eventParameter.Required);
        Assert.Equal("string", eventParameter.Schema.GetProperty("type").GetString());
        var metaParameter = subscription.Params.Single(parameter => parameter.Name == "meta");
        Assert.False(metaParameter.Required);
        Assert.Contains("null", metaParameter.Schema.GetProperty("anyOf").EnumerateArray().Select(option => option.GetProperty("type").GetString()));
        Assert.Contains("object", metaParameter.Schema.GetProperty("anyOf").EnumerateArray().Select(option => option.GetProperty("type").GetString()));
        var errorDataSchema = first.Components
            .GetProperty("schemas")
            .GetProperty("mcsl.schema.json-rpc.error.data");
        Assert.Contains("\"components\"", firstJson, StringComparison.Ordinal);
        Assert.Equal(
            ["daemon_error_kind", "correlation_id"],
            errorDataSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.False(errorDataSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            BuiltInProtocolJsonContext.Default.JsonRpcErrorData.Properties
                .Select(property => property.Name)
                .Order(),
            errorDataSchema.GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .Order());
        foreach (var optionalProperty in new[]
                 {
                     "daemon_error_code",
                     "details",
                     "origin_plugin",
                     "execution_owner"
                 })
        {
            AssertSchemaRejectsNull(errorDataSchema.GetProperty("properties").GetProperty(optionalProperty));
        }
    }

    [Fact]
    public void PublicProtocolDefinitionsDoNotRetainJsonNodeInstances()
    {
        var publicPropertiesAndFields = typeof(BuiltInProtocolDefinitions).Assembly
            .GetExportedTypes()
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(member => member is PropertyInfo or FieldInfo)
            .Select(member => member switch
            {
                PropertyInfo property => property.PropertyType,
                FieldInfo field => field.FieldType,
                _ => throw new InvalidOperationException()
            });

        Assert.DoesNotContain(typeof(JsonNode), publicPropertiesAndFields);
        Assert.All(BuiltInProtocolDefinitions.Rpcs, descriptor => Assert.DoesNotContain(descriptor.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance), field => typeof(JsonNode).IsAssignableFrom(field.FieldType)));
        Assert.All(BuiltInProtocolDefinitions.Events, descriptor => Assert.DoesNotContain(descriptor.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance), field => typeof(JsonNode).IsAssignableFrom(field.FieldType)));
    }

    private static void AssertSchemaRejectsNull(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var type))
        {
            Assert.Equal("null", schema.GetProperty("not").GetProperty("type").GetString());
            return;
        }

        if (type.ValueKind == JsonValueKind.String)
        {
            Assert.NotEqual("null", type.GetString());
            return;
        }

        Assert.Equal(JsonValueKind.Array, type.ValueKind);
        Assert.DoesNotContain("null", type.EnumerateArray().Select(element => element.GetString()));
    }

    private static void AssertCatalogChangeCondition(JsonElement condition, string operation, bool snapshotRequired)
    {
        var ifSchema = condition.GetProperty("if");
        Assert.Equal(operation, ifSchema.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString());
        Assert.Equal(["operation"], ifSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()!));

        var thenSchema = condition.GetProperty("then");
        var requiredSchema = snapshotRequired ? thenSchema : thenSchema.GetProperty("not");
        Assert.Equal(["snapshot"], requiredSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()!));
    }
}
