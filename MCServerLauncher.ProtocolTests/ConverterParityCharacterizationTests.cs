using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.DaemonClient.Serialization;
using MCServerLauncher.ProtocolTests.Fixtures.ConverterParity;
using MCServerLauncher.ProtocolTests.Helpers;
using StjJsonSerializer = System.Text.Json.JsonSerializer;

namespace MCServerLauncher.ProtocolTests;

public class ConverterParityCharacterizationTests
{
    private static readonly JsonSerializerOptions CommonStjOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new GuidStjConverter(), new EncodingStjConverter(), new PlaceHolderStringStjConverter() }
    };

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void Guid_InvalidString_DeserializesToGuidEmpty_StjBoundaryLocked()
    {
        var json = """{"id":"not-a-guid","action":"ping","params":{}}""";

        var parsed = StjJsonSerializer.Deserialize<ActionRequest>(json, DaemonClientRpcJsonBoundary.StjOptions)!;

        var actual = FixtureHarness.ParseJson(SerializeCommon(new { parsed.Id }));
        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.GuidDir, "invalid-string-deserialize.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void Guid_ValidString_RoundTripsAsCanonicalString()
    {
        var expectedGuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var json = """{"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","action":"ping","params":{}}""";

        var parsed = StjJsonSerializer.Deserialize<ActionRequest>(json, DaemonClientRpcJsonBoundary.StjOptions)!;
        var actual = FixtureHarness.ParseJson(SerializeCommon(new { parsed.Id }));
        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.GuidDir, "valid-string-roundtrip.json");

        Assert.Equal(expectedGuid, parsed.Id);
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void Encoding_ValidWebName_DeserializesToEncodingWithMatchingWebName()
    {
        var parsed = StjJsonSerializer.Deserialize<EncodingHolder>("""{"encoding":"utf-8"}""", CommonStjOptions)!;

        var actual = FixtureHarness.ParseJson(SerializeCommon(new { webName = parsed.Encoding.WebName }));
        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.EncodingDir, "valid-web-name.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void Encoding_InvalidName_ThrowsCurrentStjExceptionPath()
    {
        var ex = Record.Exception(() =>
            StjJsonSerializer.Deserialize<EncodingHolder>("""{"encoding":"definitely-invalid-encoding"}""", CommonStjOptions));

        Assert.NotNull(ex);

        var actual = FixtureHarness.ParseJson(SerializeCommon(new
        {
            exceptionType = ex!.GetType().Name,
            innerExceptionType = ex.InnerException?.GetType().Name
        }));
        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.EncodingDir, "invalid-name-exception.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void PlaceHolderString_NullEmptyNonEmpty_BehaviorMatchesCurrentStjConverter()
    {
        var nullHolder = StjJsonSerializer.Deserialize<PlaceHolderHolder>("""{"value":null}""", CommonStjOptions)!;
        var emptyHolder = StjJsonSerializer.Deserialize<PlaceHolderHolder>("""{"value":""}""", CommonStjOptions)!;
        var nonEmptyHolder = StjJsonSerializer.Deserialize<PlaceHolderHolder>("""{"value":"hello-{name}"}""", CommonStjOptions)!;

        var actual = FixtureHarness.ParseJson(SerializeCommon(new
        {
            nullIsNull = nullHolder.Value is null,
            emptyIsNull = emptyHolder.Value is null,
            nonEmptyPattern = nonEmptyHolder.Value?.Pattern
        }));

        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.PlaceHolderStringDir, "null-empty-non-empty.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void JsonPayloadBuffer_NullCreationPaths_PreserveExplicitJsonNullShape()
    {
        var fromObject = JsonPayloadBuffer.FromObject<object?>(null, CommonStjOptions);
        var fromJsonNull = new JsonPayloadBuffer(CreateJsonNullElement());

        Assert.True(fromObject.IsExplicitJsonNull);
        Assert.True(fromJsonNull.IsExplicitJsonNull);
        Assert.Equal(JsonValueKind.Null, fromObject.ValueKind);
        Assert.Equal(JsonValueKind.Null, fromJsonNull.ValueKind);
        Assert.Equal("null", fromObject.GetRawText());
        Assert.Equal(fromObject.GetRawText(), fromJsonNull.GetRawText());
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void ActionRequest_StjBoundary_PreservesCanonicalNestedPayloadShape()
    {
        var json = """{"action":"ping","id":"11111111-1111-1111-1111-111111111111","params":{"count":1,"message":"hi","flags":[true,false,null],"inner":{"empty":{},"list":[1,2,3]}}}""";

        var parsed = StjJsonSerializer.Deserialize<ActionRequest>(json, DaemonClientRpcJsonBoundary.StjOptions)!;

        Assert.NotNull(parsed.Parameter);
        Assert.Equal(JsonValueKind.Object, parsed.Parameter.Value.ValueKind);
        Assert.Equal(
            """{"count":1,"message":"hi","flags":[true,false,null],"inner":{"empty":{},"list":[1,2,3]}}""",
            parsed.Parameter.Value.GetRawText());
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void Permission_ValidAndInvalidString_BehaviorMatchesCurrentDaemonBoundary()
    {
        var validException = Record.Exception(() =>
            StjJsonSerializer.Deserialize<PermissionHolder>("""{"permission":"instance.start"}""", DaemonRpcJsonBoundary.StjOptions));
        var invalidException = Record.Exception(() =>
            StjJsonSerializer.Deserialize<PermissionHolder>("""{"permission":"!!!"}""", DaemonRpcJsonBoundary.StjOptions));

        var actual = FixtureHarness.ParseJson(SerializeCommon(new
        {
            validParses = validException is null,
            validExceptionType = validException?.GetType().Name,
            validInnerExceptionType = validException?.InnerException?.GetType().Name,
            invalidParses = invalidException is null,
            invalidExceptionType = invalidException?.GetType().Name,
            invalidInnerExceptionType = invalidException?.InnerException?.GetType().Name
        }));
        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.PermissionDir, "valid-invalid-behavior.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void Enum_SnakeCaseFormatting_StaysBaselineForActionStatusAndEventTypes()
    {
        var payload = new
        {
            actionType = ActionType.SubscribeEvent,
            requestStatus = ActionRequestStatus.Ok,
            eventType = EventType.DaemonReport
        };

        var actual = FixtureHarness.ParseJson(StjJsonSerializer.Serialize(payload, DaemonRpcJsonBoundary.StjOptions));
        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.EnumDir, "snake-case-formatting.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void RequiredAndNull_EnvelopeSemantics_StjParseMatrixIsCharacterized()
    {
        var missingActionThrows = Record.Exception(() =>
            StjJsonSerializer.Deserialize<ActionRequest>(
                """{"id":"11111111-1111-1111-1111-111111111111","params":{}}""",
                DaemonClientRpcJsonBoundary.StjOptions));

        var nullParamsParses = StjJsonSerializer.Deserialize<ActionRequest>(
            """{"action":"ping","id":"11111111-1111-1111-1111-111111111111","params":null}""",
            DaemonClientRpcJsonBoundary.StjOptions)!;

        var missingDataThrows = Record.Exception(() =>
            StjJsonSerializer.Deserialize<ActionResponse>(
                """{"status":"ok","retcode":0,"message":"OK","id":"22222222-2222-2222-2222-222222222222"}""",
                DaemonClientRpcJsonBoundary.StjOptions));

        var nullMetaParses = StjJsonSerializer.Deserialize<EventPacket>(
            """{"event":"daemon_report","meta":null,"data":{"report":{"os":{"name":"Windows","arch":"x64"},"cpu":{"vendor":"GenuineIntel","name":"Intel(R)","count":4,"usage":0.1},"mem":{"total":1024,"free":512},"drive":{"drive_format":"NTFS","total":1000,"free":500},"start_time_stamp":1717171717}},"time":1717171717}""",
            DaemonRpcJsonBoundary.StjOptions)!;

        var missingMetaThrows = Record.Exception(() =>
            StjJsonSerializer.Deserialize<EventPacket>(
                """{"event":"daemon_report","data":{"report":{"os":{"name":"Windows","arch":"x64"},"cpu":{"vendor":"GenuineIntel","name":"Intel(R)","count":4,"usage":0.1},"mem":{"total":1024,"free":512},"drive":{"drive_format":"NTFS","total":1000,"free":500},"start_time_stamp":1717171717}},"time":1717171717}""",
                DaemonRpcJsonBoundary.StjOptions));

        Assert.NotNull(missingActionThrows);
        Assert.NotNull(missingDataThrows);
        // STJ does not throw for missing optional fields (unlike Newtonsoft)

        var actual = FixtureHarness.ParseJson(SerializeCommon(new
        {
            missingActionException = missingActionThrows!.GetType().Name,
            nullParamsIsNull = nullParamsParses.Parameter is null,
            missingDataException = missingDataThrows!.GetType().Name,
            nullMetaIsNull = nullMetaParses.EventMeta is null,
            nullMetaIsExplicitJsonNull = nullMetaParses.EventMeta?.IsExplicitJsonNull == true,
            missingMetaException = missingMetaThrows?.GetType().Name
        }));

        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.EnumDir, "required-null-semantics.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void EventMetaData_NullVsMissingPolicy_IsExplicitInHelpers()
    {
        var missingMeta = EventType.InstanceLog.GetEventMeta(null);
        Assert.Null(missingMeta);

        var explicitNullMeta = JsonPayloadBuffer.FromObject(null, JsonSettings.Settings);
        var nullMetaResult = EventType.InstanceLog.GetEventMeta(explicitNullMeta);
        Assert.Null(nullMetaResult);

        var explicitNullData = JsonPayloadBuffer.FromObject(null, JsonSettings.Settings);
        var nullDataResult = EventType.DaemonReport.GetEventData(explicitNullData);
        Assert.Null(nullDataResult);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void EventMeta_Data_StjFirstDeserialization_ProducesTypedResults()
    {
        var metaJson = "{\"instance_id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"}";
        using var metaDoc = JsonDocument.Parse(metaJson);
        var metaBuffer = new JsonPayloadBuffer(metaDoc.RootElement.Clone());

        var meta = EventType.InstanceLog.GetEventMeta(metaBuffer);
        var typedMeta = Assert.IsType<InstanceLogEventMeta>(meta);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), typedMeta.InstanceId);

        var dataJson = "{\"log\":\"[12:00:00] Server started\"}";
        using var dataDoc = JsonDocument.Parse(dataJson);
        var dataBuffer = new JsonPayloadBuffer(dataDoc.RootElement.Clone());

        var data = EventType.InstanceLog.GetEventData(dataBuffer);
        var typedData = Assert.IsType<InstanceLogEventData>(data);
        Assert.Equal("[12:00:00] Server started", typedData.Log);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void JsonPayloadBuffer_StjFromObject_CreatesBufferWithCorrectShape()
    {
        var fromNull = JsonPayloadBuffer.FromObject((InstanceLogEventMeta?)null);
        Assert.True(fromNull.IsExplicitJsonNull);
        Assert.Equal(JsonValueKind.Null, fromNull.ValueKind);
        Assert.Equal("null", fromNull.GetRawText());

        var meta = new InstanceLogEventMeta
        {
            InstanceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        };
        var fromTyped = JsonPayloadBuffer.FromObject(meta, CommonStjOptions);
        Assert.Equal(JsonValueKind.Object, fromTyped.ValueKind);

        var rawText = fromTyped.GetRawText();
        Assert.Contains("instance_id", rawText);
        Assert.Contains("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", rawText);

        var fromJsonNull = new JsonPayloadBuffer(CreateJsonNullElement());
        Assert.Equal(fromJsonNull.GetRawText(), fromNull.GetRawText());
        Assert.Equal(fromJsonNull.ValueKind, fromNull.ValueKind);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void JsonPayloadBuffer_StjFromObject_NoOptions_UsesCanonicalCommonWireShape()
    {
        var payload = new DaemonReportEventData
        {
            Report = new DaemonReport(
                new OsInfo("Windows", "x64"),
                new CpuInfo("GenuineIntel", "Intel(R)", 4, 0.1),
                new MemInfo(1024, 512),
                new DriveInformation("NTFS", 1000, 500),
                1717171717)
        };

        var fromTyped = JsonPayloadBuffer.FromObject(payload);

        Assert.Equal(JsonValueKind.Object, fromTyped.ValueKind);
        Assert.Contains("start_time_stamp", fromTyped.GetRawText());
        Assert.Contains("drive_format", fromTyped.GetRawText());
    }

    private static string SerializeCommon<T>(T value)
    {
        return StjJsonSerializer.Serialize(value, CommonStjOptions);
    }

    private static JsonElement CreateJsonNullElement()
    {
        using var doc = JsonDocument.Parse("null");
        return doc.RootElement.Clone();
    }

    private sealed class EncodingHolder
    {
        [JsonPropertyName("encoding")]
        public Encoding Encoding { get; init; } = null!;
    }

    private sealed class PlaceHolderHolder
    {
        [JsonPropertyName("value")]
        public PlaceHolderString? Value { get; init; }
    }

    private sealed class PermissionHolder
    {
        [JsonPropertyName("permission")]
        public Permission Permission { get; init; } = null!;
    }
}
