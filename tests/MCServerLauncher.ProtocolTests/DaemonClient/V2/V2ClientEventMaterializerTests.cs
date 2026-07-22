using System.Text;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.DaemonClient.Connection.V2;
using MCServerLauncher.DaemonClient.Protocol;

namespace MCServerLauncher.ProtocolTests.DaemonClient.V2;

public sealed partial class V2ClientEventMaterializerTests
{
    private const string InstanceId = "11111111-1111-1111-1111-111111111111";
    private const string RuleId = "22222222-2222-2222-2222-222222222222";
    private const string CatalogItemJson = "{\"instance_id\":\"" + InstanceId + "\",\"name\":\"demo\",\"instance_type\":\"universal\",\"version\":\"1\",\"status\":\"running\",\"ready_timed_out\":false}";
    private const string SystemJson = "{\"os\":{\"name\":\"Windows\",\"architecture\":\"x64\"},\"cpu\":{\"vendor\":\"vendor\",\"name\":\"cpu\",\"count\":16,\"usage\":5.5,\"core_count\":8,\"thread_count\":16},\"mem\":{\"total_kilobytes\":32768,\"free_kilobytes\":16384},\"drive\":{\"drive_format\":\"NTFS\",\"total_bytes\":1024,\"free_bytes\":512,\"name\":\"C\"},\"drives\":[],\"daemon_version\":\"2.0.0\"}";

    [Fact]
    public void CatalogDescriptorMaterializesTypedDataAndPreservesEnvelopeFields()
    {
        var materialized = Materialize(
            V2ClientProtocol.InstanceCatalogChanged,
            JsonRpcOptionalPayload.Missing,
            Raw($"{{\"version\":7,\"operation\":\"upsert\",\"instance_id\":\"{InstanceId}\",\"snapshot\":{CatalogItemJson}}}"),
            sequence: 41,
            timestamp: 1783677000000);

        Assert.Equal(41, materialized.Sequence);
        Assert.Equal(1783677000000, materialized.Timestamp);
        Assert.Equal(DaemonEventFieldKind.Missing, materialized.Meta.Kind);
        Assert.Throws<InvalidOperationException>(() => materialized.Meta.Value);
        Assert.Equal(DaemonEventFieldKind.Value, materialized.Data.Kind);
        Assert.Equal(7, materialized.Data.Value.Version);
        Assert.Equal(Guid.Parse(InstanceId), materialized.Data.Value.InstanceId);
    }

    [Fact]
    public void CatalogDescriptorMaterializesRemoveWithoutSnapshot()
    {
        var materialized = Materialize(
            V2ClientProtocol.InstanceCatalogChanged,
            JsonRpcOptionalPayload.Missing,
            Raw($"{{\"version\":8,\"operation\":\"remove\",\"instance_id\":\"{InstanceId}\"}}"));

        Assert.Equal(InstanceCatalogChangeOperation.Remove, materialized.Data.Value.Operation);
        Assert.Null(materialized.Data.Value.Snapshot);
    }

    [Fact]
    public void DaemonReportDescriptorMaterializesItsOwnSourceGeneratedDataType()
    {
        var materialized = Materialize(
            V2ClientProtocol.DaemonReport,
            JsonRpcOptionalPayload.Missing,
            Raw($"{{\"system_info\":{SystemJson},\"start_timestamp\":123}}"));

        Assert.Equal(DaemonEventFieldKind.Missing, materialized.Meta.Kind);
        Assert.Equal(DaemonEventFieldKind.Value, materialized.Data.Kind);
        Assert.Equal(123, materialized.Data.Value.StartTimestamp);
        Assert.Equal("Windows", materialized.Data.Value.SystemInfo.Os.Name);
    }

    [Fact]
    public void InstanceLogDescriptorMaterializesTypedMetaAndData()
    {
        var materialized = Materialize(
            V2ClientProtocol.InstanceLog,
            Raw($"{{\"instance_id\":\"{InstanceId}\"}}"),
            Raw("{\"log\":\"started\"}"));

        Assert.Equal(DaemonEventFieldKind.Value, materialized.Meta.Kind);
        Assert.Equal(Guid.Parse(InstanceId), materialized.Meta.Value.InstanceId);
        Assert.Equal(DaemonEventFieldKind.Value, materialized.Data.Kind);
        Assert.Equal("started", materialized.Data.Value.Log);
    }

    [Fact]
    public void NotificationDescriptorMaterializesItsOwnMetaAndDataTypes()
    {
        var materialized = Materialize(
            V2ClientProtocol.Notification,
            Raw($"{{\"source_instance_id\":\"{InstanceId}\",\"rule_id\":\"{RuleId}\"}}"),
            Raw("{\"title\":\"title\",\"message\":\"message\",\"severity\":\"info\"}"));

        Assert.Equal(Guid.Parse(InstanceId), materialized.Meta.Value.SourceInstanceId);
        Assert.Equal(Guid.Parse(RuleId), materialized.Meta.Value.RuleId);
        Assert.Equal("title", materialized.Data.Value.Title);
        Assert.Equal("info", materialized.Data.Value.Severity);
    }

    [Fact]
    public void TypedFieldsDoNotCollapseExplicitNullIntoMissing()
    {
        var missing = DaemonEventField<string>.Missing;
        var explicitNull = DaemonEventField<string>.ExplicitNull;
        var value = DaemonEventField<string>.FromValue("value");

        Assert.Equal(DaemonEventFieldKind.Missing, missing.Kind);
        Assert.Equal(DaemonEventFieldKind.ExplicitNull, explicitNull.Kind);
        Assert.Equal(DaemonEventFieldKind.Value, value.Kind);
        Assert.Throws<InvalidOperationException>(() => missing.Value);
        Assert.Throws<InvalidOperationException>(() => explicitNull.Value);
        Assert.Equal("value", value.Value);
    }

    [Fact]
    public void OmittedMetaAndRequiredDataDescriptorsEnforceEveryWirePresenceShape()
    {
        var catalogData = Raw($"{{\"version\":1,\"operation\":\"upsert\",\"instance_id\":\"{InstanceId}\",\"snapshot\":{CatalogItemJson}}}");
        var reportData = Raw($"{{\"system_info\":{SystemJson},\"start_timestamp\":1}}");

        AssertPresenceMatrix(V2ClientProtocol.InstanceCatalogChanged, catalogData);
        AssertPresenceMatrix(V2ClientProtocol.DaemonReport, reportData);
    }

    [Fact]
    public void RequiredMetaAndDataDescriptorsEnforceEveryWirePresenceShape()
    {
        var logMeta = Raw($"{{\"instance_id\":\"{InstanceId}\"}}");
        var logData = Raw("{\"log\":\"line\"}");
        var notificationMeta = Raw($"{{\"source_instance_id\":\"{InstanceId}\",\"rule_id\":\"{RuleId}\"}}");
        var notificationData = Raw("{\"title\":\"title\",\"message\":\"message\",\"severity\":\"info\"}");

        AssertPresenceMatrix(V2ClientProtocol.InstanceLog, logMeta, logData);
        AssertPresenceMatrix(V2ClientProtocol.Notification, notificationMeta, notificationData);
    }

    [Fact]
    public void MethodMismatchReturnsStableSafeTransportError()
    {
        var result = V2ClientEventMaterializer.Materialize(
            V2ClientProtocol.InstanceLog,
            Notification(
                "mcsl.event.notification",
                Raw($"{{\"instance_id\":\"{InstanceId}\"}}"),
                Raw("{\"log\":\"secret parser text\"}")));

        AssertInvalid(result);
    }

    [Theory]
    [InlineData("{\"log\":")]
    [InlineData("{\"log\":42}")]
    [InlineData("{\"log\":\"line\",\"unknown_secret\":true}")]
    public void MalformedWrongShapeAndUnknownMembersReturnStableSafeError(string dataJson)
    {
        var result = V2ClientEventMaterializer.Materialize(
            V2ClientProtocol.InstanceLog,
            Notification(
                V2ClientProtocol.InstanceLog.Name.Value,
                Raw($"{{\"instance_id\":\"{InstanceId}\"}}"),
                Raw(dataJson)));

        AssertInvalid(result);
    }

    [Fact]
    public void NullTypedResultFromValuePayloadIsRejected()
    {
        var disguisedNull = Raw(" null ");
        Assert.Equal(JsonRpcOptionalPayloadKind.Value, disguisedNull.Kind);

        var result = V2ClientEventMaterializer.Materialize(
            V2ClientProtocol.InstanceLog,
            Notification(
                V2ClientProtocol.InstanceLog.Name.Value,
                Raw($"{{\"instance_id\":\"{InstanceId}\"}}"),
                disguisedNull));

        AssertInvalid(result);
    }

    [Fact]
    public void OptionalDescriptorUsesSuppliedSourceGeneratedMetadataForEveryWireFieldShape()
    {
        var descriptor = CreateSyntheticOptionalDescriptor();

        foreach (var meta in OptionalPayloads("{\"correlation_id\":\"meta\",\"attempt\":3}"))
        {
            foreach (var data in OptionalPayloads("{\"message\":\"data\",\"number\":7}"))
            {
                var materialized = Materialize(descriptor, meta.Payload, data.Payload);
                AssertSyntheticField(materialized.Meta, meta.Kind, "meta", 3);
                AssertSyntheticField(materialized.Data, data.Kind, "data", 7);
            }
        }

        AssertInvalid(V2ClientEventMaterializer.Materialize(
            descriptor,
            Notification(
                descriptor.Name.Value,
                Raw("{\"correlation_id\":\"meta\",\"attempt\":3}"),
                Raw("{\"message\":42,\"number\":7}"))));
        AssertInvalid(V2ClientEventMaterializer.Materialize(
            descriptor,
            Notification(
                descriptor.Name.Value,
                Raw("{\"correlation_id\":42,\"attempt\":3}"),
                Raw("{\"message\":\"data\",\"number\":7}"))));
    }

    [Fact]
    public void JsonValidCatalogUpsertWithoutSnapshotReturnsStableSafeError()
    {
        var result = V2ClientEventMaterializer.Materialize(
            V2ClientProtocol.InstanceCatalogChanged,
            Notification(
                V2ClientProtocol.InstanceCatalogChanged.Name.Value,
                JsonRpcOptionalPayload.Missing,
                Raw($"{{\"version\":9,\"operation\":\"upsert\",\"instance_id\":\"{InstanceId}\"}}")));

        AssertInvalid(result);
    }

    private static DaemonEvent<TData, TMeta> Materialize<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        JsonRpcOptionalPayload meta,
        JsonRpcOptionalPayload data,
        long sequence = 11,
        long timestamp = 12)
    {
        var result = V2ClientEventMaterializer.Materialize(
            descriptor,
            Notification(descriptor.Name.Value, meta, data, sequence, timestamp));
        Assert.True(result.IsOk(out _));
        return result.Unwrap();
    }

    private static void AssertPresenceMatrix<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        JsonRpcOptionalPayload validData)
    {
        Assert.Equal(OpenRpcEventFieldPresence.Omitted, descriptor.MetaPresence);
        Materialize(descriptor, JsonRpcOptionalPayload.Missing, validData);
        AssertInvalid(V2ClientEventMaterializer.Materialize(
            descriptor,
            Notification(descriptor.Name.Value, JsonRpcOptionalPayload.ExplicitNull, validData)));
        AssertInvalid(V2ClientEventMaterializer.Materialize(
            descriptor,
            Notification(descriptor.Name.Value, Raw("{}"), validData)));
        AssertInvalid(V2ClientEventMaterializer.Materialize(
            descriptor,
            Notification(descriptor.Name.Value, JsonRpcOptionalPayload.Missing, JsonRpcOptionalPayload.Missing)));
        AssertInvalid(V2ClientEventMaterializer.Materialize(
            descriptor,
            Notification(descriptor.Name.Value, JsonRpcOptionalPayload.Missing, JsonRpcOptionalPayload.ExplicitNull)));
    }

    private static void AssertPresenceMatrix<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        JsonRpcOptionalPayload validMeta,
        JsonRpcOptionalPayload validData)
    {
        Assert.Equal(OpenRpcEventFieldPresence.Required, descriptor.MetaPresence);
        Materialize(descriptor, validMeta, validData);
        AssertInvalid(V2ClientEventMaterializer.Materialize(
            descriptor,
            Notification(descriptor.Name.Value, JsonRpcOptionalPayload.Missing, validData)));
        AssertInvalid(V2ClientEventMaterializer.Materialize(
            descriptor,
            Notification(descriptor.Name.Value, JsonRpcOptionalPayload.ExplicitNull, validData)));
        AssertInvalid(V2ClientEventMaterializer.Materialize(
            descriptor,
            Notification(descriptor.Name.Value, validMeta, JsonRpcOptionalPayload.Missing)));
        AssertInvalid(V2ClientEventMaterializer.Materialize(
            descriptor,
            Notification(descriptor.Name.Value, validMeta, JsonRpcOptionalPayload.ExplicitNull)));
    }

    private static void AssertInvalid<TData, TMeta>(
        RustyOptions.Result<DaemonEvent<TData, TMeta>, DaemonError> result)
    {
        Assert.True(result.IsErr(out var error));
        var transportError = Assert.IsType<TransportDaemonError>(error);
        Assert.Equal(V2ClientEventMaterializer.InvalidEventCode, transportError.Code);
        Assert.Equal(V2ClientEventMaterializer.InvalidEventMessage, transportError.Message);
        Assert.Null(transportError.Details);
        Assert.DoesNotContain("secret", transportError.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static EventDescriptor<SyntheticEventData, SyntheticEventMeta> CreateSyntheticOptionalDescriptor() =>
        SyntheticEventDescriptorConstructor<SyntheticEventData, SyntheticEventMeta>.Create(
            new EventName("mcsl.event.test.synthetic.optional"),
            new PermissionName("test.read"),
            SyntheticEventJsonContext.Default.SyntheticEventData,
            SyntheticEventJsonContext.Default.SyntheticEventMeta,
            OpenRpcEventFieldPresence.Optional,
            OpenRpcEventFieldPresence.Optional,
            documentation: null);

    private static IEnumerable<(DaemonEventFieldKind Kind, JsonRpcOptionalPayload Payload)> OptionalPayloads(
        string valueJson)
    {
        yield return (DaemonEventFieldKind.Missing, JsonRpcOptionalPayload.Missing);
        yield return (DaemonEventFieldKind.ExplicitNull, JsonRpcOptionalPayload.ExplicitNull);
        yield return (DaemonEventFieldKind.Value, Raw(valueJson));
    }

    private static void AssertSyntheticField<T>(
        DaemonEventField<T> field,
        DaemonEventFieldKind expectedKind,
        string expectedText,
        int expectedNumber)
    {
        Assert.Equal(expectedKind, field.Kind);
        if (expectedKind != DaemonEventFieldKind.Value)
        {
            Assert.Throws<InvalidOperationException>(() => field.Value);
            return;
        }

        switch (field.Value)
        {
            case SyntheticEventMeta meta:
                Assert.Equal(expectedText, meta.CorrelationId);
                Assert.Equal(expectedNumber, meta.Attempt);
                break;
            case SyntheticEventData data:
                Assert.Equal(expectedText, data.Message);
                Assert.Equal(expectedNumber, data.Number);
                break;
            default:
                throw new InvalidOperationException("The synthetic descriptor materialized an unexpected type.");
        }
    }

    private static JsonRpcRemoteEventNotification Notification(
        string method,
        JsonRpcOptionalPayload meta,
        JsonRpcOptionalPayload data,
        long sequence = 11,
        long timestamp = 12) =>
        new(method, new JsonRpcRemoteEventParameters(sequence, timestamp, meta, data));

    private static JsonRpcOptionalPayload Raw(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return JsonRpcOptionalPayload.FromOwnedBuffer(bytes, 0, bytes.Length);
    }

    private sealed record SyntheticEventData(string Message, int Number);

    private sealed record SyntheticEventMeta(string CorrelationId, int Attempt);

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(SyntheticEventData))]
    [JsonSerializable(typeof(SyntheticEventMeta))]
    private partial class SyntheticEventJsonContext : JsonSerializerContext;

    private static class SyntheticEventDescriptorConstructor<TData, TMeta>
    {
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
        internal static extern EventDescriptor<TData, TMeta> Create(
            EventName name,
            PermissionName permission,
            JsonTypeInfo<TData> dataTypeInfo,
            JsonTypeInfo<TMeta>? metaTypeInfo,
            OpenRpcEventFieldPresence dataPresence,
            OpenRpcEventFieldPresence metaPresence,
            EventDocumentation? documentation);
    }
}
