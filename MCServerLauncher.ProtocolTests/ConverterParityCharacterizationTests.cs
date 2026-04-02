using System.Text;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.ProtocolTests.Fixtures.ConverterParity;
using MCServerLauncher.ProtocolTests.Helpers;
using Newtonsoft.Json;

namespace MCServerLauncher.ProtocolTests;

public class ConverterParityCharacterizationTests
{
    private static readonly JsonSerializerSettings PermissionJsonSettings = CreatePermissionJsonSettings();

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void Guid_InvalidString_DeserializesToGuidEmpty_CurrentBehaviorLocked()
    {
        // Semantic note: this behavior is converter-defined tolerance, not a schema field-name lock.
        // If later migration intentionally tightens invalid-guid handling, this test should be updated with explicit policy.
        var json = """{"id":"not-a-guid","action":"ping","params":{}}""";

        var parsed = JsonConvert.DeserializeObject<ActionRequest>(json, JsonSettings.Settings)!;

        var actual = FixtureHarness.ParseJson(JsonConvert.SerializeObject(new { parsed.Id }, JsonSettings.Settings));
        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.GuidDir, "invalid-string-deserialize.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void Guid_ValidString_RoundTripsAsCanonicalString()
    {
        var expectedGuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var json = """{"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","action":"ping","params":{}}""";

        var parsed = JsonConvert.DeserializeObject<ActionRequest>(json, JsonSettings.Settings)!;
        var actual = FixtureHarness.ParseJson(JsonConvert.SerializeObject(new { parsed.Id }, JsonSettings.Settings));
        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.GuidDir, "valid-string-roundtrip.json");

        Assert.Equal(expectedGuid, parsed.Id);
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void Encoding_ValidWebName_DeserializesToEncodingWithMatchingWebName()
    {
        var parsed = JsonConvert.DeserializeObject<EncodingHolder>("""{"encoding":"utf-8"}""", JsonSettings.Settings)!;

        var actual = FixtureHarness.ParseJson(JsonConvert.SerializeObject(new { webName = parsed.Encoding.WebName }, JsonSettings.Settings));
        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.EncodingDir, "valid-web-name.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void Encoding_InvalidName_ThrowsArgumentException_CurrentBehaviorLocked()
    {
        var ex = Record.Exception(() =>
            JsonConvert.DeserializeObject<EncodingHolder>("""{"encoding":"definitely-invalid-encoding"}""", JsonSettings.Settings));

        Assert.NotNull(ex);

        var actual = FixtureHarness.ParseJson(JsonConvert.SerializeObject(new
        {
            exceptionType = ex!.GetType().Name,
            innerExceptionType = ex.InnerException?.GetType().Name
        }, JsonSettings.Settings));
        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.EncodingDir, "invalid-name-exception.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void PlaceHolderString_NullEmptyNonEmpty_BehaviorMatchesCurrentConverter()
    {
        var nullHolder = JsonConvert.DeserializeObject<PlaceHolderHolder>("""{"value":null}""", JsonSettings.Settings)!;
        var emptyHolder = JsonConvert.DeserializeObject<PlaceHolderHolder>("""{"value":""}""", JsonSettings.Settings)!;
        var nonEmptyHolder = JsonConvert.DeserializeObject<PlaceHolderHolder>("""{"value":"hello-{name}"}""", JsonSettings.Settings)!;

        var actual = FixtureHarness.ParseJson(JsonConvert.SerializeObject(new
        {
            nullIsNull = nullHolder.Value is null,
            emptyIsNull = emptyHolder.Value is null,
            nonEmptyPattern = nonEmptyHolder.Value?.Pattern
        }, JsonSettings.Settings));

        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.PlaceHolderStringDir, "null-empty-non-empty.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void Permission_ValidAndInvalidString_BehaviorMatchesCurrentDaemonConverter()
    {
        var valid = JsonConvert.DeserializeObject<PermissionHolder>("""{"permission":"instance.start"}""", PermissionJsonSettings)!;
        var invalidException = Record.Exception(() =>
            JsonConvert.DeserializeObject<PermissionHolder>("""{"permission":"!!!"}""", PermissionJsonSettings));

        var actual = FixtureHarness.ParseJson(JsonConvert.SerializeObject(new
        {
            valid = valid.Permission.ToString(),
            invalidThrows = invalidException is not null,
            invalidExceptionType = invalidException?.GetType().Name,
            invalidInnerExceptionType = invalidException?.InnerException?.GetType().Name
        }, JsonSettings.Settings));
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

        var actual = FixtureHarness.ParseJson(JsonConvert.SerializeObject(payload, JsonSettings.Settings));
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
        var missingActionThrows = Assert.Throws<JsonSerializationException>(() =>
            JsonConvert.DeserializeObject<ActionRequest>("""{"id":"11111111-1111-1111-1111-111111111111","params":{}}""", JsonSettings.Settings));

        var nullParamsParses = JsonConvert.DeserializeObject<ActionRequest>(
            """{"action":"ping","id":"11111111-1111-1111-1111-111111111111","params":null}""",
            JsonSettings.Settings)!;

        var missingDataThrows = Assert.Throws<JsonSerializationException>(() =>
            JsonConvert.DeserializeObject<ActionResponse>(
                """{"status":"ok","retcode":0,"message":"OK","id":"22222222-2222-2222-2222-222222222222"}""",
                JsonSettings.Settings));

        var nullMetaParses = JsonConvert.DeserializeObject<EventPacket>(
            """{"event":"daemon_report","meta":null,"data":{"report":{"os":{"name":"Windows","arch":"x64"},"cpu":{"vendor":"GenuineIntel","name":"Intel(R)","count":4,"usage":0.1},"mem":{"total":1024,"free":512},"drive":{"drive_format":"NTFS","total":1000,"free":500},"start_time_stamp":1717171717}},"time":1717171717}""",
            JsonSettings.Settings)!;

        var missingMetaThrows = Assert.Throws<JsonSerializationException>(() =>
            JsonConvert.DeserializeObject<EventPacket>(
                """{"event":"daemon_report","data":{"report":{"os":{"name":"Windows","arch":"x64"},"cpu":{"vendor":"GenuineIntel","name":"Intel(R)","count":4,"usage":0.1},"mem":{"total":1024,"free":512},"drive":{"drive_format":"NTFS","total":1000,"free":500},"start_time_stamp":1717171717}},"time":1717171717}""",
                JsonSettings.Settings));

        var actual = FixtureHarness.ParseJson(JsonConvert.SerializeObject(new
        {
            missingActionException = missingActionThrows.GetType().Name,
            nullParamsIsNull = nullParamsParses.Parameter is null,
            missingDataException = missingDataThrows.GetType().Name,
            nullMetaIsNull = nullMetaParses.EventMeta is null,
            nullMetaIsExplicitJsonNull = nullMetaParses.EventMeta?.IsExplicitJsonNull == true,
            missingMetaException = missingMetaThrows.GetType().Name
        }, JsonSettings.Settings));

        var expected = FixtureHarness.LoadFixture(ConverterParityFixturePaths.EnumDir, "required-null-semantics.json");
        FixtureHarness.AssertStructuralEquals(expected, actual);
    }

    [Fact]
    [Trait("Category", "ConverterParity")]
    public void EventMetaData_NullVsMissingPolicy_IsExplicitInHelpers()
    {
        var missingMeta = EventType.InstanceLog.GetEventMeta(null, JsonSettings.Settings);
        Assert.Null(missingMeta);

        var explicitNullMeta = JsonPayloadBuffer.FromObject(null, JsonSettings.Settings);
        var metaException = Assert.Throws<ArgumentException>(() =>
            EventType.InstanceLog.GetEventMeta(explicitNullMeta, JsonSettings.Settings));
        Assert.Contains("explicit json null", metaException.Message, StringComparison.OrdinalIgnoreCase);

        var explicitNullData = JsonPayloadBuffer.FromObject(null, JsonSettings.Settings);
        var dataException = Assert.Throws<ArgumentException>(() =>
            EventType.DaemonReport.GetEventData(explicitNullData, JsonSettings.Settings));
        Assert.Contains("explicit json null", dataException.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonSerializerSettings CreatePermissionJsonSettings()
    {
        var settings = new JsonSerializerSettings(JsonSettings.Settings);
        settings.Converters.Add(new Permission.PermissionJsonConverter());
        return settings;
    }

    private sealed class EncodingHolder
    {
        [JsonProperty("encoding")]
        public UTF8Encoding Encoding { get; init; } = null!;
    }

    private sealed class PlaceHolderHolder
    {
        [JsonProperty("value")]
        public PlaceHolderString? Value { get; init; }
    }

    private sealed class PermissionHolder
    {
        [JsonProperty("permission")]
        public Permission Permission { get; init; } = null!;
    }
}
