using System.Text.Json;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.ProtocolTests.Fixtures.Persistence;
using MCServerLauncher.ProtocolTests.Helpers;

namespace MCServerLauncher.ProtocolTests;

public class EventRuleDiscriminatorCharacterizationTests
{
    [Fact]
    [Trait("Category", "EventRuleKnown")]
    public void EventRuleKnown_DedicatedFixture_DeserializesKnownDiscriminatorsAndRoundTrips()
    {
        var fixturePath = Path.Combine(PersistenceFixturePaths.EventRuleDir, "known-discriminators-event-rule.json");
        var fixture = FixtureHarness.LoadFixture(fixturePath);

        var parsed = System.Text.Json.JsonSerializer.Deserialize<EventRule>(fixture.GetRawText(), StjResolver.CreateDefaultOptions());
        Assert.NotNull(parsed);

        Assert.Collection(parsed!.Rulesets,
            ruleset => Assert.IsType<AlwaysTrueRuleset>(ruleset),
            ruleset => Assert.IsType<AlwaysFalseRuleset>(ruleset),
            ruleset => Assert.IsType<InstanceStatusRuleset>(ruleset));

        Assert.Collection(parsed.Triggers,
            trigger => Assert.IsType<ConsoleOutputTrigger>(trigger),
            trigger => Assert.IsType<ScheduleTrigger>(trigger),
            trigger => Assert.IsType<InstanceStatusTrigger>(trigger));

        Assert.Collection(parsed.Actions,
            action => Assert.IsType<SendCommandAction>(action),
            action => Assert.IsType<ChangeInstanceStatusAction>(action),
            action => Assert.IsType<SendNotificationAction>(action));

        var canonical = System.Text.Json.JsonSerializer.Serialize(parsed, new JsonSerializerOptions(StjResolver.CreateDefaultOptions()) { WriteIndented = true });
        FixtureHarness.AssertStructuralEquals(fixture, FixtureHarness.ParseJson(canonical),
            "known EventRule discriminators should remain compatible");
    }

    [Fact]
    [Trait("Category", "EventRuleKnown")]
    public void EventRuleKnown_DaemonDocumentCodec_RoundTripsKnownDiscriminators()
    {
        var fixturePath = Path.Combine(PersistenceFixturePaths.EventRuleDir, "known-discriminators-event-rule.json");
        var fixture = FixtureHarness.LoadFixture(fixturePath);

        var rules = EventRuleDocumentCodec.DeserializeRequired(LoadEventRuleDocumentFixture(fixturePath));
        var parsed = Assert.Single(rules);

        Assert.Collection(parsed.Rulesets,
            ruleset => Assert.IsType<AlwaysTrueRuleset>(ruleset),
            ruleset => Assert.IsType<AlwaysFalseRuleset>(ruleset),
            ruleset => Assert.IsType<InstanceStatusRuleset>(ruleset));

        Assert.Collection(parsed.Triggers,
            trigger => Assert.IsType<ConsoleOutputTrigger>(trigger),
            trigger => Assert.IsType<ScheduleTrigger>(trigger),
            trigger => Assert.IsType<InstanceStatusTrigger>(trigger));

        Assert.Collection(parsed.Actions,
            action => Assert.IsType<SendCommandAction>(action),
            action => Assert.IsType<ChangeInstanceStatusAction>(action),
            action => Assert.IsType<SendNotificationAction>(action));

        var canonical = EventRuleDocumentCodec.SerializeToElement(rules);
        FixtureHarness.AssertStructuralEquals(fixture, canonical[0],
            "known EventRule discriminators should round-trip through the daemon document codec");
    }

    [Fact]
    [Trait("Category", "EventRuleUnknown")]
    public void EventRuleUnknown_DaemonDocumentCodec_UnknownTriggerDiscriminator_ThrowsExplicitFailure()
    {
        var fixturePath = Path.Combine(PersistenceFixturePaths.EventRuleDir, "unknown-trigger-discriminator-event-rule.json");
        var ex = Assert.Throws<JsonException>(() =>
            EventRuleDocumentCodec.DeserializeRequired(LoadEventRuleDocumentFixture(fixturePath)));

        Assert.Contains("Unknown TriggerDefinition discriminator 'FutureTrigger'", ex.Message);
        Assert.Contains("Known values: ConsoleOutput, Schedule, InstanceStatus", ex.Message);
    }

    [Fact]
    [Trait("Category", "EventRuleUnknown")]
    public void EventRuleUnknown_UnknownTriggerDiscriminator_ThrowsExplicitFailure()
    {
        var ex = Assert.Throws<JsonException>(() => DeserializeEventRuleFixture("unknown-trigger-discriminator-event-rule.json"));

        Assert.Contains("Unknown TriggerDefinition discriminator 'FutureTrigger'", ex.Message);
        Assert.Contains("Known values: ConsoleOutput, Schedule, InstanceStatus", ex.Message);
    }

    [Fact]
    [Trait("Category", "EventRuleUnknown")]
    public void EventRuleUnknown_MissingRulesetDiscriminator_ThrowsExplicitFailure()
    {
        var ex = Assert.Throws<JsonException>(() => DeserializeEventRuleFixture("missing-ruleset-discriminator-event-rule.json"));

        Assert.Contains("Missing discriminator 'type' for RulesetDefinition", ex.Message);
    }

    [Fact]
    [Trait("Category", "EventRuleUnknown")]
    public void EventRuleUnknown_InvalidActionDiscriminatorType_ThrowsExplicitFailure()
    {
        var ex = Assert.Throws<JsonException>(() => DeserializeEventRuleFixture("invalid-action-discriminator-event-rule.json"));

        Assert.Contains("Invalid discriminator 'type' for ActionDefinition: expected string", ex.Message);
    }

    private static EventRule DeserializeEventRuleFixture(string fixtureFile)
    {
        var fixturePath = Path.Combine(PersistenceFixturePaths.EventRuleDir, fixtureFile);
        return Assert.Single(EventRuleDocumentCodec.DeserializeRequired(LoadEventRuleDocumentFixture(fixturePath)));
    }

    private static JsonElement LoadEventRuleDocumentFixture(string fixturePath)
    {
        var json = File.ReadAllText(fixturePath);
        using var document = JsonDocument.Parse($"[{json}]");
        return document.RootElement.Clone();
    }
}
