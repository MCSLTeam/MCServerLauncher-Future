using System.Text;
using System.Text.Json;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.ProtocolTests.Fixtures.ConverterParity;
using MCServerLauncher.ProtocolTests.Helpers;

namespace MCServerLauncher.ProtocolTests;

public class ConverterParityCharacterizationTests
{
    private static readonly JsonSerializerOptions StjOpts = StjResolver.CreateDefaultOptions();
    private static readonly JsonSerializerOptions SnakeCaseOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static readonly JsonSerializerOptions TestOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters =
        {
            new GuidStjConverter(),
            new EncodingStjConverter(),
            new PlaceHolderStringStjConverter(),
            new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)
        }
    };
    private static readonly JsonSerializerOptions PermissionJsonSettings = CreatePermissionJsonSettings();

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void Guid_InvalidString_DeserializesToGuidEmpty_CurrentBehaviorLocked()
    {
        // Semantic note: this behavior is converter-defined tolerance, not a schema field-name lock.
        // If later migration intentionally tightens invalid-guid handling, this test should be updated with explicit policy.
        var json = """{"id":"not-a-guid","action":"ping","params":{}}""";

        var parsed = JsonSerializer.Deserialize<ActionRequest>(json, StjOpts)!;

        var actual = FixtureHarness.ParseJson(JsonSerializer.Serialize(new { parsed.Id }, SnakeCaseOpts));
        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.GuidDir, "invalid-string-deserialize.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void Guid_ValidString_RoundTripsAsCanonicalString()
    {
        var expectedGuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var json = """{"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","action":"ping","params":{}}""";

        var parsed = JsonSerializer.Deserialize<ActionRequest>(json, StjOpts)!;
        var actual = FixtureHarness.ParseJson(JsonSerializer.Serialize(new { parsed.Id }, SnakeCaseOpts));
        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.GuidDir, "valid-string-roundtrip.json");

        Assert.Equal(expectedGuid, parsed.Id);
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void Encoding_ValidWebName_DeserializesToEncodingWithMatchingWebName()
    {
        var parsed = JsonSerializer.Deserialize<EncodingHolder>("""{"encoding":"utf-8"}""", TestOpts)!;

        var actual = FixtureHarness.ParseJson(JsonSerializer.Serialize(new { webName = parsed.Encoding.WebName }, SnakeCaseOpts));
        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.EncodingDir, "valid-web-name.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void Encoding_InvalidName_ThrowsArgumentException_CurrentBehaviorLocked()
    {
        var ex = Record.Exception(() =>
            JsonSerializer.Deserialize<EncodingHolder>("""{"encoding":"definitely-invalid-encoding"}""", TestOpts));

        Assert.NotNull(ex);

        var actual = FixtureHarness.ParseJson(JsonSerializer.Serialize(new
        {
            exceptionType = ex!.GetType().Name,
            innerExceptionType = ex.InnerException?.GetType().Name
        }, SnakeCaseOpts));
        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.EncodingDir, "invalid-name-exception.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void PlaceHolderString_NullEmptyNonEmpty_BehaviorMatchesCurrentConverter()
    {
        var nullHolder = JsonSerializer.Deserialize<PlaceHolderHolder>("""{"value":null}""", TestOpts)!;
        var emptyHolder = JsonSerializer.Deserialize<PlaceHolderHolder>("""{"value":""}""", TestOpts)!;
        var nonEmptyHolder = JsonSerializer.Deserialize<PlaceHolderHolder>("""{"value":"hello-{name}"}""", TestOpts)!;

        var actual = FixtureHarness.ParseJson(JsonSerializer.Serialize(new
        {
            nullIsNull = nullHolder.Value is null,
            emptyIsNull = emptyHolder.Value is null,
            nonEmptyPattern = nonEmptyHolder.Value?.Pattern
        }, SnakeCaseOpts));

        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.PlaceHolderStringDir, "null-empty-non-empty.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void JsonPayloadBuffer_NullCreationPaths_PreserveExplicitJsonNullShape()
    {
        var fromObject = JsonPayloadBuffer.FromObject<object>(null);
        var fromJToken = JsonPayloadBuffer.FromObject<object>(null);

        Assert.True(fromObject.IsExplicitJsonNull);
        Assert.True(fromJToken.IsExplicitJsonNull);
        Assert.Equal(JsonValueKind.Null, fromObject.ValueKind);
        Assert.Equal(JsonValueKind.Null, fromJToken.ValueKind);
        Assert.Equal("null", fromObject.GetRawText());
        Assert.Equal(fromObject.GetRawText(), fromJToken.GetRawText());
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void JsonElementConverter_PreservesCanonicalNestedPayloadShape()
    {
        var json = """{"action":"ping","id":"11111111-1111-1111-1111-111111111111","params":{"count":1,"message":"hi","flags":[true,false,null],"inner":{"empty":{},"list":[1,2,3]}}}""";

        var parsed = System.Text.Json.JsonSerializer.Deserialize<ActionRequest>(json, StjResolver.CreateDefaultOptions())!;

        Assert.NotNull(parsed.Parameter);
        Assert.Equal(JsonValueKind.Object, parsed.Parameter.Value.ValueKind);
        Assert.Equal(
            """{"count":1,"message":"hi","flags":[true,false,null],"inner":{"empty":{},"list":[1,2,3]}}""",
            parsed.Parameter.Value.GetRawText());
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void Permission_ValidAndInvalidString_BehaviorMatchesCurrentDaemonConverter()
    {
        var valid = System.Text.Json.JsonSerializer.Deserialize<PermissionHolder>("""{"permission":"instance.start"}""", PermissionJsonSettings)!;
        var invalidException = Record.Exception(() =>
            System.Text.Json.JsonSerializer.Deserialize<PermissionHolder>("""{"permission":"!!!"}""", PermissionJsonSettings));

        var actual = FixtureHarness.ParseJson(JsonSerializer.Serialize(new
        {
            valid = valid.Permission.ToString(),
            invalidThrows = invalidException is not null,
            invalidExceptionType = invalidException?.GetType().Name,
            invalidInnerExceptionType = invalidException?.InnerException?.GetType().Name
        }, SnakeCaseOpts));
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

        var actual = FixtureHarness.ParseJson(JsonSerializer.Serialize(payload, TestOpts));
        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.EnumDir, "snake-case-formatting.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void RequiredAndNull_EnvelopeSemantics_CurrentParseMatrixIsCharacterized()
    {
        // Schema-lock vs cleanup marker:
        // - Missing required envelope fields are lock-level failures.
        // - `params` explicit null now maps to C# null for ActionRequest payload buffering.
        // - `data` is now explicitly required in ActionResponse envelope even when null is allowed.
        var opts = StjResolver.CreateDefaultOptions();

        var missingActionThrows = Assert.Throws<JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<ActionRequest>("""{"id":"11111111-1111-1111-1111-111111111111","params":{}}""", opts));

        var nullParamsParses = System.Text.Json.JsonSerializer.Deserialize<ActionRequest>(
            """{"action":"ping","id":"11111111-1111-1111-1111-111111111111","params":null}""",
            opts)!;

        // Missing "data" throws — Data has [SysTextJsonRequired]
        var missingDataThrows = Assert.Throws<JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<ActionResponse>(
                """{"status":"ok","retcode":0,"message":"OK","id":"22222222-2222-2222-2222-222222222222"}""",
                opts));

        var nullMetaParses = System.Text.Json.JsonSerializer.Deserialize<EventPacket>(
            """{"event":"daemon_report","meta":null,"data":{"report":{"os":{"name":"Windows","arch":"x64"},"cpu":{"vendor":"GenuineIntel","name":"Intel(R)","count":4,"usage":0.1},"mem":{"total":1024,"free":512},"drive":{"drive_format":"NTFS","total":1000,"free":500},"start_time_stamp":1717171717}},"time":1717171717}""",
            opts)!;

        // Missing "meta" no longer throws — nullable JsonPayloadBuffer? fields accept missing keys in STJ
        var missingMetaParses = System.Text.Json.JsonSerializer.Deserialize<EventPacket>(
                """{"event":"daemon_report","data":{"report":{"os":{"name":"Windows","arch":"x64"},"cpu":{"vendor":"GenuineIntel","name":"Intel(R)","count":4,"usage":0.1},"mem":{"total":1024,"free":512},"drive":{"drive_format":"NTFS","total":1000,"free":500},"start_time_stamp":1717171717}},"time":1717171717}""",
                opts)!;

        var actual = FixtureHarness.ParseJson(System.Text.Json.JsonSerializer.Serialize(new
        {
            missingActionException = missingActionThrows.GetType().Name,
            nullParamsIsNull = nullParamsParses.Parameter is null,
            missingDataException = missingDataThrows.GetType().Name,
            nullMetaIsNull = nullMetaParses.EventMeta is null,
            nullMetaIsExplicitJsonNull = nullMetaParses.EventMeta?.IsExplicitJsonNull == true,
            missingMetaIsNull = missingMetaParses.EventMeta is null
        }, new JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower }));

        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.EnumDir, "required-null-semantics.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void EventMetaData_NullVsMissingPolicy_IsExplicitInHelpers()
    {
        var missingMeta = EventType.InstanceLog.GetEventMeta(null);
        Assert.Null(missingMeta);

        var explicitNullMeta = JsonPayloadBuffer.FromObject<object>(null);
        var nullMetaResult = EventType.InstanceLog.GetEventMeta(explicitNullMeta);
        Assert.Null(nullMetaResult);

        var explicitNullData = JsonPayloadBuffer.FromObject<object>(null);
        var nullDataResult = EventType.DaemonReport.GetEventData(explicitNullData);
        Assert.Null(nullDataResult);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void EventMeta_Data_StjFirstDeserialization_ProducesTypedResults()
    {
        // Verify that the STJ-first path deserializes typed event meta correctly
        var metaJson = "{\"instance_id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"}";
        using var metaDoc = System.Text.Json.JsonDocument.Parse(metaJson);
        var metaBuffer = new JsonPayloadBuffer(metaDoc.RootElement.Clone());

        var meta = EventType.InstanceLog.GetEventMeta(metaBuffer);
        var typedMeta = Assert.IsType<InstanceLogEventMeta>(meta);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), typedMeta.InstanceId);

        // Verify that the STJ-first path deserializes typed event data correctly
        var dataJson = "{\"log\":\"[12:00:00] Server started\"}";
        using var dataDoc = System.Text.Json.JsonDocument.Parse(dataJson);
        var dataBuffer = new JsonPayloadBuffer(dataDoc.RootElement.Clone());

        var data = EventType.InstanceLog.GetEventData(dataBuffer);
        var typedData = Assert.IsType<InstanceLogEventData>(data);
        Assert.Equal("[12:00:00] Server started", typedData.Log);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void JsonPayloadBuffer_StjFromObject_CreatesBufferWithCorrectShape()
    {
        // STJ-first creation path: null produces explicit JSON null
        var fromNull = JsonPayloadBuffer.FromObject((InstanceLogEventMeta?)null);
        Assert.True(fromNull.IsExplicitJsonNull);
        Assert.Equal(JsonValueKind.Null, fromNull.ValueKind);
        Assert.Equal("null", fromNull.GetRawText());

        // STJ-first creation path: typed value produces correct wire-shape JSON
        var stjOptions = StjResolver.CreateDefaultOptions();
        var meta = new InstanceLogEventMeta
        {
            InstanceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        };
        var fromTyped = JsonPayloadBuffer.FromObject(meta, stjOptions);
        Assert.Equal(JsonValueKind.Object, fromTyped.ValueKind);

        var rawText = fromTyped.GetRawText();
        Assert.Contains("instance_id", rawText);
        Assert.Contains("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", rawText);

        // Both null creation paths produce the same result
        var fromAlternateNull = JsonPayloadBuffer.FromObject<object>(null);
        Assert.Equal(fromAlternateNull.GetRawText(), fromNull.GetRawText());
        Assert.Equal(fromAlternateNull.ValueKind, fromNull.ValueKind);
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

    private static JsonSerializerOptions CreatePermissionJsonSettings()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
            Converters = { new Permission.PermissionJsonConverter() }
        };
    }

    private sealed class EncodingHolder
    {
        [System.Text.Json.Serialization.JsonPropertyName("encoding")]
        [System.Text.Json.Serialization.JsonConverter(typeof(EncodingStjConverter))]
        public Encoding Encoding { get; init; } = null!;
    }

    private sealed class PlaceHolderHolder
    {
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public PlaceHolderString? Value { get; init; }
    }

    private sealed class PermissionHolder
    {
        [System.Text.Json.Serialization.JsonPropertyName("permission")]
        [System.Text.Json.Serialization.JsonConverter(typeof(Permission.PermissionJsonConverter))]
        public Permission Permission { get; init; } = null!;
    }
}
