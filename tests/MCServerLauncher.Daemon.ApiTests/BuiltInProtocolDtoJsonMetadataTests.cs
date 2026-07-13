using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Daemon.ApiTests;

public sealed class BuiltInProtocolDtoJsonMetadataTests
{
    [Fact]
    public void EveryBuiltInProtocolWireDtoHasProductionSourceGeneratedMetadata()
    {
        var context = BuiltInProtocolJsonContext.Default;
        var wireTypes = new[]
        {
            typeof(EmptyRequest),
            typeof(UnitResult),
            typeof(PingResult),
            typeof(PermissionsResult),
            typeof(EventSubscriptionRequest),
            typeof(JsonRpcErrorObject),
            typeof(JsonRpcErrorData),
            typeof(ProtocolOwnerIdentity),
            typeof(UploadChunkAcknowledgement),
            typeof(FileSessionReference),
            typeof(DownloadReadResult),
            typeof(InstanceCatalogItem),
            typeof(InstanceCatalogResult),
            typeof(InstanceCatalogChangedEventData),
            typeof(InstanceLogEventMeta),
            typeof(InstanceLogEventData),
            typeof(DaemonReportEventData),
            typeof(NotificationEventMeta),
            typeof(NotificationEventData),
            typeof(OpenRpcInfo),
            typeof(OpenRpcContentDescriptor),
            typeof(OpenRpcMethod),
            typeof(OpenRpcEventField),
            typeof(OpenRpcEvent),
            typeof(OpenRpcDocument),
            typeof(JsonRpcRequestId),
            typeof(JsonRpcObjectPayload),
            typeof(JsonRpcRequestEnvelope),
            typeof(JsonRpcSuccessResponseEnvelope),
            typeof(JsonRpcErrorResponseEnvelope),
            typeof(JsonRpcOptionalPayload),
            typeof(JsonRpcRemoteEventParameters),
            typeof(JsonRpcRemoteEventNotification),
            typeof(JsonRpcUploadAcknowledgementNotification)
        };

        Assert.All(wireTypes, type => Assert.NotNull(context.GetTypeInfo(type)));
    }

    [Fact]
    public void EmptyRequestAndUnitResultSerializeAsEmptyObjects()
    {
        Assert.Equal("{}", JsonSerializer.Serialize(new EmptyRequest(), BuiltInProtocolJsonContext.Default.EmptyRequest));
        Assert.Equal("{}", JsonSerializer.Serialize(new UnitResult(), BuiltInProtocolJsonContext.Default.UnitResult));
    }

    [Fact]
    public void EventSubscriptionRequestPreservesMissingNullAndObjectMetaStates()
    {
        var context = BuiltInProtocolJsonContext.Default.EventSubscriptionRequest;

        var missing = new EventSubscriptionRequest("mcsl.event.instance.log");
        var explicitNull = new EventSubscriptionRequest("mcsl.event.instance.log", EventMetaFilter.ExplicitNull);
        var objectFilter = new EventSubscriptionRequest(
            "mcsl.event.instance.log",
            EventMetaFilter.FromObject("{\"instance_id\":\"11111111-1111-1111-1111-111111111111\"}"u8));

        var missingJson = JsonSerializer.Serialize(missing, context);
        var nullJson = JsonSerializer.Serialize(explicitNull, context);
        var objectJson = JsonSerializer.Serialize(objectFilter, context);

        Assert.Equal("{\"event\":\"mcsl.event.instance.log\"}", missingJson);
        Assert.Equal("{\"event\":\"mcsl.event.instance.log\",\"meta\":null}", nullJson);
        Assert.Equal(
            "{\"event\":\"mcsl.event.instance.log\",\"meta\":{\"instance_id\":\"11111111-1111-1111-1111-111111111111\"}}",
            objectJson);

        Assert.Equal(EventMetaFilterKind.Missing, JsonSerializer.Deserialize(missingJson, context)!.Meta.Kind);
        Assert.Equal(EventMetaFilterKind.ExplicitNull, JsonSerializer.Deserialize(nullJson, context)!.Meta.Kind);
        var parsedObject = JsonSerializer.Deserialize(objectJson, context)!;
        Assert.Equal(EventMetaFilterKind.Object, parsedObject.Meta.Kind);
        Assert.Equal("{\"instance_id\":\"11111111-1111-1111-1111-111111111111\"}", System.Text.Encoding.UTF8.GetString(parsedObject.Meta.ObjectUtf8Json.AsSpan()));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"event\":\"a\",\"meta\":[]}", context));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"event\":\"a\",\"event\":\"b\"}", context));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"event\":\"a\",\"unknown\":true}", context));
    }

    [Fact]
    public void CatalogContractsSortSnapshotsAndEnforceChangeInvariants()
    {
        var first = new InstanceCatalogItem(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "first",
            InstanceType.MCJava,
            "1.21.5",
            InstanceStatus.Running);
        var second = new InstanceCatalogItem(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "second",
            InstanceType.MCJava,
            "1.21.5",
            InstanceStatus.Stopped);

        var catalog = new InstanceCatalogResult(0, [second, first]);

        Assert.Equal([first.InstanceId, second.InstanceId], catalog.Items.Select(item => item.InstanceId));
        Assert.Throws<ArgumentException>(() => new InstanceCatalogResult(0, [first, first]));
        Assert.Throws<ArgumentException>(() => new InstanceCatalogChangedEventData(
            1,
            InstanceCatalogChangeOperation.Upsert,
            first.InstanceId,
            second));
        Assert.Throws<ArgumentException>(() => new InstanceCatalogChangedEventData(
            1,
            InstanceCatalogChangeOperation.Remove,
            first.InstanceId,
            first));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InstanceCatalogItem(
            first.InstanceId,
            first.Name,
            (InstanceType)999,
            first.Version,
            first.Status));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InstanceCatalogChangedEventData(
            1,
            (InstanceCatalogChangeOperation)999,
            first.InstanceId,
            null));

        var remove = new InstanceCatalogChangedEventData(
            1,
            InstanceCatalogChangeOperation.Remove,
            first.InstanceId,
            null);
        var removeJson = JsonSerializer.Serialize(remove, BuiltInProtocolJsonContext.Default.InstanceCatalogChangedEventData);
        Assert.DoesNotContain("snapshot", removeJson, StringComparison.Ordinal);
        Assert.Contains("\"operation\":\"remove\"", removeJson, StringComparison.Ordinal);
    }

    [Fact]
    public void UploadAndDownloadControlDtosEnforceTheirWireRules()
    {
        var sessionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var error = new JsonRpcErrorObject(
            -32000,
            "Upload failed.",
            new JsonRpcErrorData("file.write_failed", DaemonErrorWireKind.Storage, "correlation", null, null, null));

        Assert.Throws<ArgumentException>(() => new UploadChunkAcknowledgement(
            sessionId,
            0,
            1,
            UploadChunkAcknowledgementStatus.Accepted,
            error));
        Assert.Throws<ArgumentNullException>(() => new UploadChunkAcknowledgement(
            sessionId,
            0,
            1,
            UploadChunkAcknowledgementStatus.Rejected,
            null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DownloadReadResult(sessionId, -1, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UploadChunkAcknowledgement(
            sessionId,
            0,
            1,
            (UploadChunkAcknowledgementStatus)999,
            null));

        var acceptedJson = JsonSerializer.Serialize(
            new UploadChunkAcknowledgement(sessionId, 0, 1024, UploadChunkAcknowledgementStatus.Accepted, null),
            BuiltInProtocolJsonContext.Default.UploadChunkAcknowledgement);
        Assert.Contains("\"status\":\"accepted\"", acceptedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("error", acceptedJson, StringComparison.Ordinal);

        var readJson = JsonSerializer.Serialize(
            new DownloadReadResult(sessionId, 0, 1024, false),
            BuiltInProtocolJsonContext.Default.DownloadReadResult);
        Assert.Equal(
            "{\"session_id\":\"33333333-3333-3333-3333-333333333333\",\"offset\":0,\"length\":1024,\"is_final\":false}",
            readJson);
        Assert.DoesNotContain("data", readJson, StringComparison.Ordinal);
    }

    [Fact]
    public void BuiltInProtocolEnumsAreStringOnlyAndRejectUndefinedValues()
    {
        var context = BuiltInProtocolJsonContext.Default;
        var sessionId = "33333333-3333-3333-3333-333333333333";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            $$"""{"session_id":"{{sessionId}}","offset":0,"length":1,"status":0}""",
            context.UploadChunkAcknowledgement));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"version\":1,\"operation\":0,\"instance_id\":\"11111111-1111-1111-1111-111111111111\"}",
            context.InstanceCatalogChangedEventData));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"presence\":0}",
            context.OpenRpcEventField));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"instance_id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"first\",\"instance_type\":0,\"version\":\"1.21.5\",\"status\":\"running\"}",
            context.InstanceCatalogItem));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenRpcEventField((OpenRpcEventFieldPresence)999, null));
    }

    [Fact]
    public void JsonRpcErrorContractsRequireValidatedErrorData()
    {
        var errorDataTypeInfo = BuiltInProtocolJsonContext.Default.JsonRpcErrorData;
        Assert.Equal(JsonTypeInfoKind.Object, errorDataTypeInfo.Kind);
        Assert.Equal(
            ["correlation_id", "daemon_error_code", "daemon_error_kind", "details", "execution_owner", "origin_plugin"],
            errorDataTypeInfo.Properties.Select(property => property.Name).Order());

        Assert.Throws<ArgumentException>(() => new JsonRpcErrorObject(-32000, " ", new JsonRpcErrorData(null, DaemonErrorWireKind.Internal, "correlation", null, null, null)));
        Assert.Throws<ArgumentNullException>(() => new JsonRpcErrorObject(-32000, "failed", null!));
        Assert.Throws<ArgumentException>(() => new JsonRpcErrorData(null, DaemonErrorWireKind.Internal, " ", null, null, null));
        Assert.Throws<ArgumentException>(() => new JsonRpcErrorData(" ", DaemonErrorWireKind.Internal, "correlation", null, null, null));
        Assert.Throws<ArgumentException>(() => new JsonRpcErrorData(null, DaemonErrorWireKind.Internal, "correlation", default(JsonElement), null, null));
        Assert.Throws<ArgumentException>(() => new ProtocolOwnerIdentity(" ", "1.0.0"));
        Assert.Throws<ArgumentException>(() => new ProtocolOwnerIdentity("plugin", " "));

        var error = new JsonRpcErrorObject(
            -32000,
            "failed",
            new JsonRpcErrorData(null, DaemonErrorWireKind.Internal, "correlation", null, null, null));
        var json = JsonSerializer.Serialize(error, BuiltInProtocolJsonContext.Default.JsonRpcErrorObject);
        Assert.Equal("{\"code\":-32000,\"message\":\"failed\",\"data\":{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"correlation\"}}", json);
        var roundTrip = JsonSerializer.Deserialize(json, BuiltInProtocolJsonContext.Default.JsonRpcErrorObject)!;
        Assert.Equal("correlation", roundTrip.Data.CorrelationId);
        Assert.Null(roundTrip.Data.DaemonErrorCode);
        Assert.Null(roundTrip.Data.Details);
        Assert.Null(roundTrip.Data.OriginPlugin);
        Assert.Null(roundTrip.Data.ExecutionOwner);
        var directRoundTrip = JsonSerializer.Deserialize(
            "{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"correlation\"}",
            BuiltInProtocolJsonContext.Default.JsonRpcErrorData)!;
        Assert.Equal("correlation", directRoundTrip.CorrelationId);
        Assert.Null(directRoundTrip.DaemonErrorCode);
        Assert.Null(directRoundTrip.Details);
        Assert.Null(directRoundTrip.OriginPlugin);
        Assert.Null(directRoundTrip.ExecutionOwner);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"code\":-32000,\"message\":\"failed\",\"data\":{}}",
            BuiltInProtocolJsonContext.Default.JsonRpcErrorObject));

        var publicConstructor = Assert.Single(typeof(JsonRpcErrorData).GetConstructors());
        Assert.All(publicConstructor.GetParameters(), parameter =>
        {
            Assert.False(parameter.IsOptional);
            Assert.False(parameter.HasDefaultValue);
        });

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"code\":-32000,\"message\":\"failed\",\"data\":null}",
            BuiltInProtocolJsonContext.Default.JsonRpcErrorObject));
        foreach (var explicitNullProperty in new[]
                 {
                     "\"daemon_error_code\":null",
                     "\"details\":null",
                     "\"origin_plugin\":null",
                     "\"execution_owner\":null"
                 })
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
                $"{{\"code\":-32000,\"message\":\"failed\",\"data\":{{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"correlation\",{explicitNullProperty}}}}}",
                BuiltInProtocolJsonContext.Default.JsonRpcErrorObject));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
                $"{{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"correlation\",{explicitNullProperty}}}",
                BuiltInProtocolJsonContext.Default.JsonRpcErrorData));
        }

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"daemon_error_kind\":null,\"correlation_id\":\"correlation\"}",
            BuiltInProtocolJsonContext.Default.JsonRpcErrorData));

        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize(
            "{\"code\":-32000,\"message\":\" \",\"data\":{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"correlation\"}}",
            BuiltInProtocolJsonContext.Default.JsonRpcErrorObject));
    }

    [Fact]
    public void OpenRpcDocumentUsesTheFrozenExtensionFieldsAndClonesSchemas()
    {
        OpenRpcContentDescriptor descriptor;
        OpenRpcEventField data;
        using (var document = JsonDocument.Parse("{\"type\":\"object\"}"))
        {
            descriptor = new OpenRpcContentDescriptor("request", document.RootElement, true, null, null);
            data = new OpenRpcEventField(OpenRpcEventFieldPresence.Required, document.RootElement);
        }

        var meta = new OpenRpcEventField(OpenRpcEventFieldPresence.Omitted, null);
        var documentDto = new OpenRpcDocument(
            "1.3.2",
            new OpenRpcInfo("MCServerLauncher daemon", "2.0.0"),
            [new OpenRpcMethod("mcsl.daemon.ping", [descriptor], descriptor, "*", false, null, null)],
            [new OpenRpcEvent("mcsl.event.instance.log", "*", data, meta, null, null)]);

        var json = JsonSerializer.Serialize(documentDto, BuiltInProtocolJsonContext.Default.OpenRpcDocument);

        Assert.Contains("\"openrpc\":\"1.3.2\"", json, StringComparison.Ordinal);
        Assert.Contains("\"paramStructure\":\"by-name\"", json, StringComparison.Ordinal);
        Assert.Contains("\"x-mcsl-permission\":\"*\"", json, StringComparison.Ordinal);
        Assert.Contains("\"x-mcsl-allow-notification\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"x-mcsl-events\"", json, StringComparison.Ordinal);
        Assert.Contains("\"presence\":\"omitted\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"schema\":null", json, StringComparison.Ordinal);
        Assert.Equal("object", descriptor.Schema.GetProperty("type").GetString());

        using var parsed = JsonDocument.Parse(json);
        var method = parsed.RootElement.GetProperty("methods")[0];
        Assert.Equal("*", method.GetProperty("x-mcsl-permission").GetString());
        var @event = parsed.RootElement.GetProperty("x-mcsl-events")[0];
        Assert.Equal("*", @event.GetProperty("permission").GetString());
        Assert.False(@event.TryGetProperty("x-mcsl-permission", out _));
    }

    [Fact]
    public void NotificationMetadataRequiresBothPublicIdentifiers()
    {
        Assert.Throws<ArgumentException>(() => new NotificationEventMeta(Guid.Empty, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new NotificationEventMeta(Guid.NewGuid(), Guid.Empty));
    }
}
