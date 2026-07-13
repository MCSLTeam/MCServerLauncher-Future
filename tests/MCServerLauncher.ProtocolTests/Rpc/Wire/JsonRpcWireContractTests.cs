using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;

namespace MCServerLauncher.ProtocolTests.Rpc.Wire;

public sealed class JsonRpcWireContractTests
{
    private static readonly BuiltInProtocolJsonContext Json = BuiltInProtocolJsonContext.Default;

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("-0", 0L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    public void IntegerRequestIdsRoundTripWithTheirValidatedToken(string token, long expectedValue)
    {
        var id = JsonSerializer.Deserialize(token, Json.JsonRpcRequestId)!;

        Assert.Equal(JsonRpcRequestIdKind.Integer, id.Kind);
        Assert.Equal(expectedValue, id.IntegerValue);
        Assert.Equal(token, id.IntegerToken);
        Assert.Equal(token, JsonSerializer.Serialize(id, Json.JsonRpcRequestId));
    }

    [Fact]
    public void StringRequestIdsRoundTripTheirValueAndEscaping()
    {
        const string json = "\"request\\u002did\\n\\\"escaped\\\"\"";

        var id = JsonSerializer.Deserialize(json, Json.JsonRpcRequestId)!;
        var roundTrip = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(id, Json.JsonRpcRequestId),
            Json.JsonRpcRequestId)!;

        Assert.Equal("request-id\n\"escaped\"", id.StringValue);
        Assert.Equal(id, roundTrip);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("1.0")]
    [InlineData("1e0")]
    [InlineData("9223372036854775808")]
    [InlineData("-9223372036854775809")]
    public void RequestIdsRejectValuesOutsideTheFrozenProfile(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, Json.JsonRpcRequestId));
    }

    [Fact]
    public void RequestEnvelopeRequiresTheProfileAndObjectParameters()
    {
        const string request =
            "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.daemon.ping\",\"id\":-0,\"params\":{}}";

        var parsed = JsonRpcWireParser.ParseRequest(System.Text.Encoding.UTF8.GetBytes(request));

        Assert.False(parsed.IsNotification);
        Assert.Equal("-0", parsed.Id!.IntegerToken);
        Assert.Equal(request, JsonSerializer.Serialize(parsed, Json.JsonRpcRequestEnvelope));

        var notification = new JsonRpcRequestEnvelope("mcsl.example.notification", null);
        Assert.Equal(
            "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.example.notification\"}",
            JsonSerializer.Serialize(notification, Json.JsonRpcRequestEnvelope));

        foreach (var invalid in new[]
                 {
                     "{\"jsonrpc\":\"1.0\",\"method\":\"x\",\"id\":1}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"x\",\"id\":null}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"x\",\"params\":null}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"x\",\"params\":[]}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"x\",\"unknown\":{}}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"x\",\"method\":\"y\"}"
                 })
        {
            var exception = Assert.Throws<JsonRpcRequestParseException>(() =>
                JsonRpcWireParser.ParseRequest(System.Text.Encoding.UTF8.GetBytes(invalid)));
            Assert.Equal(JsonRpcRequestFailureKind.InvalidRequest, exception.FailureKind);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"jsonrpc\":\"2.0\"")]
    [InlineData("@")]
    [InlineData("{} {}")]
    public void RequestParserClassifiesMalformedOrTrailingJsonAsParseError(string json)
    {
        var exception = Assert.Throws<JsonRpcRequestParseException>(() =>
            JsonRpcWireParser.ParseRequest(System.Text.Encoding.UTF8.GetBytes(json)));

        Assert.Equal(JsonRpcRequestFailureKind.ParseError, exception.FailureKind);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("\"request\"")]
    [InlineData("{}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"method\":\"x\",\"params\":[]}")]
    public void RequestParserClassifiesValidNonProfileJsonAsInvalidRequest(string json)
    {
        var exception = Assert.Throws<JsonRpcRequestParseException>(() =>
            JsonRpcWireParser.ParseRequest(System.Text.Encoding.UTF8.GetBytes(json)));

        Assert.Equal(JsonRpcRequestFailureKind.InvalidRequest, exception.FailureKind);
    }

    [Fact]
    public void ObjectPayloadUsesOnlyCallerSuppliedNonGenericMetadataAndExactTypes()
    {
        var payload = JsonRpcWireParser.ParseRequest(System.Text.Encoding.UTF8.GetBytes(
            "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.daemon.ping\",\"id\":1,\"params\":{}}"))
            .Params!;

        var value = payload.Deserialize(Json.EmptyRequest);
        Assert.IsType<EmptyRequest>(value);
        Assert.Throws<JsonException>(() => payload.Deserialize(Json.PingResult));
        Assert.Throws<ArgumentException>(() => JsonRpcObjectPayload.From(new EmptyRequest(), Json.PingResult));
    }

    [Fact]
    public void SuccessAndErrorResponsesHaveMutuallyExclusiveShapesAndAllowNullIds()
    {
        var id = JsonRpcRequestId.FromString("request-1");
        var success = new JsonRpcSuccessResponseEnvelope(
            id,
            JsonRpcObjectPayload.From(new UnitResult(), Json.UnitResult));
        var parseError = new JsonRpcErrorResponseEnvelope(
            null,
            new JsonRpcErrorObject(
                -32700,
                "Parse error.",
                new JsonRpcErrorData(null, DaemonErrorWireKind.Internal, "correlation", null, null, null)));

        var successJson = JsonSerializer.Serialize(success, Json.JsonRpcSuccessResponseEnvelope);
        var errorJson = JsonSerializer.Serialize(parseError, Json.JsonRpcErrorResponseEnvelope);

        Assert.Equal("{\"jsonrpc\":\"2.0\",\"id\":\"request-1\",\"result\":{}}", successJson);
        Assert.DoesNotContain("error", successJson, StringComparison.Ordinal);
        Assert.Equal(
            "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32700,\"message\":\"Parse error.\",\"data\":{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"correlation\"}}}",
            errorJson);
        Assert.DoesNotContain("result", errorJson, StringComparison.Ordinal);

        var parsedSuccess = JsonRpcWireParser.ParseSuccessResponse(System.Text.Encoding.UTF8.GetBytes(successJson));
        var parsedError = JsonRpcWireParser.ParseErrorResponse(System.Text.Encoding.UTF8.GetBytes(errorJson));
        Assert.Equal(id, parsedSuccess.Id);
        Assert.Null(parsedError.Id);
        Assert.Throws<ArgumentNullException>(() => new JsonRpcSuccessResponseEnvelope(
            null!,
            JsonRpcObjectPayload.From(new UnitResult(), Json.UnitResult)));
    }

    [Fact]
    public void StrictParserPreservesValidatedPayloadTokensWithoutDomNormalization()
    {
        const string request =
            "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.example.read\",\"id\":1,\"params\":{ \"number\" : 1e2 }}";
        const string response =
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{ \"number\" : 1e2 }}";

        Assert.Equal(request, JsonSerializer.Serialize(
            JsonRpcWireParser.ParseRequest(System.Text.Encoding.UTF8.GetBytes(request)),
            Json.JsonRpcRequestEnvelope));
        Assert.Equal(response, JsonSerializer.Serialize(
            JsonRpcWireParser.ParseSuccessResponse(System.Text.Encoding.UTF8.GetBytes(response)),
            Json.JsonRpcSuccessResponseEnvelope));
    }

    [Fact]
    public void StrictResponseParsersRejectProfileShapeAndExclusivityViolations()
    {
        foreach (var invalid in new[]
                 {
                     "{}",
                     "{\"jsonrpc\":\"1.0\",\"id\":1,\"result\":{}}",
                     "{\"jsonrpc\":\"2.0\",\"id\":null,\"result\":{}}",
                     "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{},\"error\":{}}",
                     "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":null}",
                     "{\"jsonrpc\":\"2.0\",\"id\":1,\"id\":2,\"result\":{}}",
                     "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{},\"unknown\":true}"
                 })
        {
            Assert.Throws<JsonException>(() =>
                JsonRpcWireParser.ParseSuccessResponse(System.Text.Encoding.UTF8.GetBytes(invalid)));
        }

        const string error =
            "{\"code\":-32700,\"message\":\"Parse error.\",\"data\":{\"correlation_id\":\"c\"}}";
        foreach (var invalid in new[]
                 {
                     "{\"jsonrpc\":\"2.0\",\"error\":" + error + "}",
                     "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":null}",
                     "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":" + error + ",\"result\":{}}",
                     "{\"jsonrpc\":\"2.0\",\"id\":null,\"id\":1,\"error\":" + error + "}",
                     "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":" + error + ",\"unknown\":true}"
                 })
        {
            Assert.Throws<JsonException>(() =>
                JsonRpcWireParser.ParseErrorResponse(System.Text.Encoding.UTF8.GetBytes(invalid)));
        }
    }

    [Fact]
    public void SourceGeneratedOuterMetadataIsWriteOnlyAndCannotBypassStrictParsing()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.daemon.ping\",\"id\":1}",
            Json.JsonRpcRequestEnvelope));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}",
            Json.JsonRpcSuccessResponseEnvelope));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32700,\"message\":\"error\",\"data\":{\"correlation_id\":\"c\"}}}",
            Json.JsonRpcErrorResponseEnvelope));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.instance.log\",\"params\":{\"sequence\":1,\"timestamp\":2}}",
            Json.JsonRpcRemoteEventNotification));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.upload.ack\",\"params\":{\"session_id\":\"33333333-3333-3333-3333-333333333333\",\"offset\":0,\"length\":1,\"status\":\"accepted\"}}",
            Json.JsonRpcUploadAcknowledgementNotification));
    }

    [Fact]
    public void StrictErrorParserRoundTripsFullTypedDataAndOpenDetails()
    {
        const string json =
            "{\"jsonrpc\":\"2.0\",\"id\":\"request-1\",\"error\":{\"code\":-32005,\"message\":\"Plugin failed.\",\"data\":{\"daemon_error_code\":\"plugin.failed\",\"daemon_error_kind\":\"internal\",\"correlation_id\":\"correlation\",\"details\":{\"extension_key\":{\"nested\":[1,true,null]}},\"origin_plugin\":{\"id\":\"community.health\",\"version\":\"1.0.0\"},\"execution_owner\":{\"id\":\"community.proxy\",\"version\":\"2.0.0\"}}}}";

        var parsed = JsonRpcWireParser.ParseErrorResponse(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.Equal(-32005, parsed.Error.Code);
        Assert.Equal("plugin.failed", parsed.Error.Data.DaemonErrorCode);
        Assert.Equal(DaemonErrorWireKind.Internal, parsed.Error.Data.DaemonErrorKind);
        Assert.Equal("community.health", parsed.Error.Data.OriginPlugin!.Id);
        Assert.Equal("community.proxy", parsed.Error.Data.ExecutionOwner!.Id);
        Assert.Equal(json, JsonSerializer.Serialize(parsed, Json.JsonRpcErrorResponseEnvelope));
    }

    [Fact]
    public void StrictErrorParserRejectsInvalidNestedErrorDataAndOwners()
    {
        const string prefix = "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":";
        foreach (var invalidError in new[]
                 {
                     "{\"message\":\"error\",\"data\":{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"c\"}}",
                     "{\"code\":1.0,\"message\":\"error\",\"data\":{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"c\"}}",
                     "{\"code\":2147483648,\"message\":\"error\",\"data\":{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"c\"}}",
                     "{\"code\":-32000,\"message\":null,\"data\":{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"c\"}}",
                     "{\"code\":-32000,\"message\":\" \",\"data\":{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"c\"}}",
                     "{\"code\":-32000,\"message\":\"error\"}",
                     "{\"code\":-32000,\"message\":\"error\",\"data\":null}",
                     "{\"code\":-32000,\"message\":\"error\",\"data\":{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"c\"},\"unknown\":true}",
                     "{\"code\":-32000,\"code\":-32001,\"message\":\"error\",\"data\":{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"c\"}}"
                 })
        {
            Assert.Throws<JsonException>(() => JsonRpcWireParser.ParseErrorResponse(
                System.Text.Encoding.UTF8.GetBytes(prefix + invalidError + "}")));
        }

        foreach (var invalidData in new[]
                 {
                     "{\"daemon_error_kind\":\"internal\",\"correlation_id\":null}",
                     "{\"daemon_error_kind\":\"internal\",\"correlation_id\":\" \"}",
                     "{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"c\",\"correlation_id\":\"d\"}",
                     "{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"c\",\"daemon_error_code\":null}",
                     "{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"c\",\"details\":null}",
                     "{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"c\",\"details\":{\"duplicate\":1,\"duplicate\":2}}",
                     "{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"c\",\"origin_plugin\":null}",
                     "{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"c\",\"origin_plugin\":{\"id\":\"plugin\"}}",
                     "{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"c\",\"origin_plugin\":{\"id\":\"plugin\",\"version\":null}}",
                     "{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"c\",\"origin_plugin\":{\"id\":\"plugin\",\"version\":\"1\",\"unknown\":true}}",
                     "{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"c\",\"execution_owner\":{\"id\":\"owner\",\"id\":\"other\",\"version\":\"1\"}}",
                     "{\"daemon_error_kind\":\"internal\",\"correlation_id\":\"c\",\"unknown\":true}"
                 })
        {
            var error = "{\"code\":-32000,\"message\":\"error\",\"data\":" + invalidData + "}";
            Assert.Throws<JsonException>(() => JsonRpcWireParser.ParseErrorResponse(
                System.Text.Encoding.UTF8.GetBytes(prefix + error + "}")));
        }

        foreach (var invalidKindData in new[]
                 {
                     "{\"correlation_id\":\"c\"}",
                     "{\"daemon_error_kind\":null,\"correlation_id\":\"c\"}",
                     "{\"daemon_error_kind\":1,\"correlation_id\":\"c\"}",
                     "{\"daemon_error_kind\":\"plugin\",\"correlation_id\":\"c\"}",
                     "{\"daemon_error_kind\":\"internal\",\"daemon_error_kind\":\"transport\",\"correlation_id\":\"c\"}"
                 })
        {
            var error = "{\"code\":-32000,\"message\":\"error\",\"data\":" + invalidKindData + "}";
            Assert.Throws<JsonException>(() => JsonRpcWireParser.ParseErrorResponse(
                System.Text.Encoding.UTF8.GetBytes(prefix + error + "}")));
        }
    }

    [Theory]
    [InlineData("1")]
    [InlineData("[1,true,null,{\"extension\":\"value\"}]")]
    [InlineData("{\"extension\":{\"nested\":[1,2,3]}}")]
    public void ErrorDetailsAllowAnyNonNullJsonValueAndRoundTripStrictly(string detailsJson)
    {
        using var document = JsonDocument.Parse(detailsJson);
        var response = new JsonRpcErrorResponseEnvelope(
            JsonRpcRequestId.FromInt64(1),
            new JsonRpcErrorObject(
                -32000,
                "error",
                new JsonRpcErrorData("domain.error", DaemonErrorWireKind.Internal, "correlation", document.RootElement, null, null)));

        var json = JsonSerializer.Serialize(response, Json.JsonRpcErrorResponseEnvelope);
        var parsed = JsonRpcWireParser.ParseErrorResponse(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.Equal(detailsJson, parsed.Error.Data.Details!.Value.GetRawText());
        Assert.Equal(json, JsonSerializer.Serialize(parsed, Json.JsonRpcErrorResponseEnvelope));
    }

    [Fact]
    public void ErrorContractsRejectWriterIncompatibleStatesAtConstruction()
    {
        using var nullDetails = JsonDocument.Parse("null");
        using var duplicateDetails = JsonDocument.Parse("{\"duplicate\":1,\"duplicate\":2}");

        Assert.Throws<ArgumentException>(() =>
            new JsonRpcErrorData(" ", DaemonErrorWireKind.Internal, "correlation", null, null, null));
        Assert.Throws<ArgumentException>(() =>
            new JsonRpcErrorData(null, DaemonErrorWireKind.Internal, "correlation", default(JsonElement), null, null));
        Assert.Throws<ArgumentException>(() =>
            new JsonRpcErrorData(null, DaemonErrorWireKind.Internal, "correlation", nullDetails.RootElement, null, null));
        Assert.Throws<ArgumentException>(() =>
            new JsonRpcErrorData(null, DaemonErrorWireKind.Internal, "correlation", duplicateDetails.RootElement, null, null));
        Assert.Throws<ArgumentException>(() => new ProtocolOwnerIdentity(" ", "1.0.0"));
        Assert.Throws<ArgumentException>(() => new ProtocolOwnerIdentity("plugin", " "));
    }

    [Fact]
    public void RemoteEventsPreserveMissingNullAndTypedValueStates()
    {
        var missing = new JsonRpcRemoteEventNotification(
            "mcsl.event.daemon.report",
            new JsonRpcRemoteEventParameters(
                1,
                1783677000000,
                JsonRpcOptionalPayload.Missing,
                JsonRpcOptionalPayload.Missing));
        var nullAndValue = new JsonRpcRemoteEventNotification(
            "mcsl.event.instance.log",
            new JsonRpcRemoteEventParameters(
                2,
                1783677000001,
                JsonRpcOptionalPayload.ExplicitNull,
                JsonRpcOptionalPayload.From(new UnitResult(), Json.UnitResult)));

        Assert.Equal(
            "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.daemon.report\",\"params\":{\"sequence\":1,\"timestamp\":1783677000000}}",
            JsonSerializer.Serialize(missing, Json.JsonRpcRemoteEventNotification));
        Assert.Equal(
            "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.instance.log\",\"params\":{\"sequence\":2,\"timestamp\":1783677000001,\"meta\":null,\"data\":{}}}",
            JsonSerializer.Serialize(nullAndValue, Json.JsonRpcRemoteEventNotification));

        var parsed = JsonRpcWireParser.ParseRemoteEventNotification(
            "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.instance.log\",\"params\":{\"sequence\":3,\"timestamp\":4,\"meta\":null,\"data\":{}}}"u8);
        Assert.Equal(JsonRpcOptionalPayloadKind.ExplicitNull, parsed.Params.Meta.Kind);
        Assert.Equal(JsonRpcOptionalPayloadKind.Value, parsed.Params.Data.Kind);

        var plugin = JsonRpcWireParser.ParseRemoteEventNotification(
            "{\"jsonrpc\":\"2.0\",\"method\":\"plugin.community.instance-health.event.changed\",\"params\":{\"sequence\":5,\"timestamp\":6}}"u8);
        Assert.Equal("plugin.community.instance-health.event.changed", plugin.Method);
    }

    [Fact]
    public void OptionalPayloadDeserializesAllStatesWithExplicitMetadata()
    {
        Assert.Throws<InvalidOperationException>(() =>
            JsonRpcOptionalPayload.Missing.Deserialize(Json.PingResult));
        Assert.Null(JsonRpcOptionalPayload.ExplicitNull.Deserialize(Json.PingResult));
        Assert.Equal(0, JsonRpcOptionalPayload.ExplicitNull.Deserialize(Json.Int32));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("null"u8, Json.Int32));

        var value = JsonRpcOptionalPayload.From(new PingResult(42), Json.PingResult);

        Assert.Equal(42, value.Deserialize(Json.PingResult)!.Time);
    }

    [Fact]
    public void OptionalPayloadDeserializePublishesNullableGenericReturnMetadata()
    {
        var method = typeof(JsonRpcOptionalPayload).GetMethod(nameof(JsonRpcOptionalPayload.Deserialize))!;
        var nullableContext = method.GetCustomAttributesData().Single(attribute =>
            attribute.AttributeType.FullName == "System.Runtime.CompilerServices.NullableContextAttribute");
        var state = Assert.Single(nullableContext.ConstructorArguments);

        Assert.Equal((byte)2, Assert.IsType<byte>(state.Value));
    }

    [Fact]
    public void ParsedOptionalPayloadOwnsItsInputAndPropagatesTypedJsonErrors()
    {
        var source = System.Text.Encoding.UTF8.GetBytes(
            "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.daemon.report\",\"params\":{\"sequence\":1,\"timestamp\":2,\"data\":{\"time\":42}}}");
        var parsed = JsonRpcWireParser.ParseRemoteEventNotification(source);
        source.AsSpan().Fill((byte)' ');

        Assert.Equal(42, parsed.Params.Data.Deserialize(Json.PingResult)!.Time);

        var incompatible = JsonRpcWireParser.ParseRemoteEventNotification(
            "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.daemon.report\",\"params\":{\"sequence\":1,\"timestamp\":2,\"data\":{\"time\":\"invalid\"}}}"u8);
        Assert.Throws<JsonException>(() => incompatible.Params.Data.Deserialize(Json.PingResult));
    }

    [Fact]
    public void OptionalPayloadDeserializeRejectsNullMetadataBeforeStateDispatch()
    {
        Assert.Throws<ArgumentNullException>(() =>
            JsonRpcOptionalPayload.Missing.Deserialize<PingResult>(null!));
    }

    [Fact]
    public void StrictRemoteEventParserRejectsInvalidOuterAndParamsShapes()
    {
        foreach (var invalid in new[]
                 {
                     "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.instance.log\",\"id\":1,\"params\":{\"sequence\":1,\"timestamp\":2}}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.upload.ack\",\"params\":{\"sequence\":1,\"timestamp\":2}}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.Bad\",\"params\":{\"sequence\":1,\"timestamp\":2}}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"plugin..event.changed\",\"params\":{\"sequence\":1,\"timestamp\":2}}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"plugin.Bad.event.changed\",\"params\":{\"sequence\":1,\"timestamp\":2}}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"plugin.bad-.event.changed\",\"params\":{\"sequence\":1,\"timestamp\":2}}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"plugin.community.rpc.changed\",\"params\":{\"sequence\":1,\"timestamp\":2}}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"plugins.community.event.changed\",\"params\":{\"sequence\":1,\"timestamp\":2}}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"plugin.community.event.\",\"params\":{\"sequence\":1,\"timestamp\":2}}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.instance.log\",\"params\":null}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.instance.log\",\"params\":[]}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.instance.log\",\"params\":{\"timestamp\":2}}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.instance.log\",\"params\":{\"sequence\":1.0,\"timestamp\":2}}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.instance.log\",\"params\":{\"sequence\":1,\"timestamp\":2,\"timestamp\":3}}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.instance.log\",\"params\":{\"sequence\":1,\"timestamp\":2,\"unknown\":true}}"
                 })
        {
            Assert.Throws<JsonException>(() =>
                JsonRpcWireParser.ParseRemoteEventNotification(System.Text.Encoding.UTF8.GetBytes(invalid)));
        }
    }

    [Fact]
    public void UploadAcknowledgementsUseTheConnectionOwnedControlNotification()
    {
        var notification = new JsonRpcUploadAcknowledgementNotification(
            new UploadChunkAcknowledgement(
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                16,
                4,
                UploadChunkAcknowledgementStatus.Accepted,
                null));

        Assert.Equal(
            "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.upload.ack\",\"params\":{\"session_id\":\"33333333-3333-3333-3333-333333333333\",\"offset\":16,\"length\":4,\"status\":\"accepted\"}}",
            JsonSerializer.Serialize(notification, Json.JsonRpcUploadAcknowledgementNotification));

        var parsed = JsonRpcWireParser.ParseUploadAcknowledgementNotification(
            "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.upload.ack\",\"params\":{\"session_id\":\"33333333-3333-3333-3333-333333333333\",\"offset\":16,\"length\":4,\"status\":\"accepted\"}}"u8);
        Assert.Equal(notification.Params.SessionId, parsed.Params.SessionId);
    }

    [Fact]
    public void StrictUploadAcknowledgementParserRejectsInvalidMethodAndParamsShapes()
    {
        const string prefix = "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.upload.ack\",\"params\":";
        foreach (var invalid in new[]
                 {
                     "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.instance.log\",\"params\":{}}",
                     "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.upload.ack\",\"id\":1,\"params\":{}}",
                     prefix + "null}",
                     prefix + "[]}",
                     prefix + "{\"session_id\":\"33333333-3333-3333-3333-333333333333\",\"offset\":0,\"length\":1}}",
                     prefix + "{\"session_id\":\"33333333-3333-3333-3333-333333333333\",\"offset\":0,\"length\":1,\"status\":\"accepted\",\"status\":\"rejected\"}}",
                     prefix + "{\"session_id\":\"33333333-3333-3333-3333-333333333333\",\"offset\":0,\"length\":1.0,\"status\":\"accepted\"}}",
                     prefix + "{\"session_id\":\"33333333-3333-3333-3333-333333333333\",\"offset\":0,\"length\":1,\"status\":\"rejected\"}}",
                     prefix + "{\"session_id\":\"33333333-3333-3333-3333-333333333333\",\"offset\":0,\"length\":1,\"status\":\"accepted\",\"error\":{\"code\":-32000,\"message\":\"error\",\"data\":{\"correlation_id\":\"c\"}}}}",
                     prefix + "{\"session_id\":\"33333333-3333-3333-3333-333333333333\",\"offset\":0,\"length\":1,\"status\":\"accepted\",\"unknown\":true}}"
                 })
        {
            Assert.Throws<JsonException>(() =>
                JsonRpcWireParser.ParseUploadAcknowledgementNotification(System.Text.Encoding.UTF8.GetBytes(invalid)));
        }
    }
}
