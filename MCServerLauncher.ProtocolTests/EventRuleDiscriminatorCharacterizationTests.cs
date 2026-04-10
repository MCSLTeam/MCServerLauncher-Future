using System.Text.Json;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.EventTrigger;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.ProtocolTests.Fixtures.Persistence;
using MCServerLauncher.ProtocolTests.Fixtures.Rpc;
using MCServerLauncher.ProtocolTests.Helpers;
using Newtonsoft.Json;

namespace MCServerLauncher.ProtocolTests;

public class EventRuleDiscriminatorCharacterizationTests
{
    [Fact]
    [Trait("Category", "EventRuleKnown")]
    public void EventRuleKnown_DedicatedFixture_DeserializesKnownDiscriminatorsAndRoundTrips()
    {
        var fixturePath = Path.Combine(PersistenceFixturePaths.EventRuleDir, "known-discriminators-event-rule.json");
        var fixture = FixtureHarness.LoadFixture(fixturePath);

        var parsed = JsonConvert.DeserializeObject<EventRule>(fixture.GetRawText(), JsonSettings.Settings);
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

        var canonical = JsonConvert.SerializeObject(parsed, Formatting.Indented, JsonSettings.Settings);
        FixtureHarness.AssertStructuralEquals(fixture, FixtureHarness.ParseJson(canonical),
            "known EventRule discriminators should remain compatible");
    }

    [Fact]
    [Trait("Category", "EventRuleKnown")]
    public void EventRuleKnown_SystemTextJson_DedicatedFixture_RoundTripsKnownDiscriminators()
    {
        var fixturePath = Path.Combine(PersistenceFixturePaths.EventRuleDir, "known-discriminators-event-rule.json");
        var fixture = FixtureHarness.LoadFixture(fixturePath);

        var options = DaemonPersistenceJsonBoundary.CreateStjOptions();
        var parsed = System.Text.Json.JsonSerializer.Deserialize<EventRule>(fixture.GetRawText(), options);
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

        var canonical = System.Text.Json.JsonSerializer.Serialize(parsed, options);
        FixtureHarness.AssertStructuralEquals(fixture, FixtureHarness.ParseJson(canonical),
            "known EventRule discriminators should round-trip through System.Text.Json too");
    }

    [Fact]
    [Trait("Category", "EventRuleUnknown")]
    public void EventRuleUnknown_SystemTextJson_UnknownTriggerDiscriminator_ThrowsExplicitFailure()
    {
        var fixturePath = Path.Combine(PersistenceFixturePaths.EventRuleDir, "unknown-trigger-discriminator-event-rule.json");
        var fixture = FixtureHarness.LoadFixture(fixturePath);

        var ex = Assert.Throws<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<EventRule>(fixture.GetRawText(), DaemonPersistenceJsonBoundary.StjOptions));

        Assert.Contains("Unknown TriggerDefinition discriminator 'FutureTrigger'", ex.Message);
        Assert.Contains("Known values: ConsoleOutput, Schedule, InstanceStatus", ex.Message);
    }

    [Fact]
    [Trait("Category", "EventRuleKnown")]
    public void EventRuleKnown_PersistenceAndRpcFixtures_RemainCompatible()
    {
        var persistenceJson = File.ReadAllText(Path.Combine(
            PersistenceFixturePaths.InstanceConfigDir,
            "event-rule-heavy-daemon-instance.json"));
        var persistenceParsed = JsonConvert.DeserializeObject<InstanceConfig>(persistenceJson, JsonSettings.Settings);

        Assert.NotNull(persistenceParsed);
        Assert.NotEmpty(persistenceParsed!.EventRules);
        Assert.All(persistenceParsed.EventRules, rule =>
        {
            Assert.All(rule.Rulesets, ruleset => Assert.IsAssignableFrom<RulesetDefinition>(ruleset));
            Assert.All(rule.Triggers, trigger => Assert.IsAssignableFrom<TriggerDefinition>(trigger));
            Assert.All(rule.Actions, action => Assert.IsAssignableFrom<ActionDefinition>(action));
        });

        var rpcJson = File.ReadAllText(Path.Combine(
            RpcFixturePaths.ActionRequestDir,
            "save-event-rules-nested-parameter.json"));
        var request = JsonConvert.DeserializeObject<ActionRequest>(rpcJson, JsonSettings.Settings);
        Assert.NotNull(request);
        Assert.NotNull(request!.Parameter);

        var parameter = JsonConvert.DeserializeObject<SaveEventRulesParameter>(
            request.Parameter.Value.GetRawText(),
            JsonSettings.Settings);
        Assert.NotNull(parameter);
        Assert.NotEmpty(parameter!.Rules);

        var nestedRule = parameter.Rules[0];
        Assert.Single(nestedRule.Triggers);
        Assert.IsType<ConsoleOutputTrigger>(nestedRule.Triggers[0]);
        Assert.Single(nestedRule.Rulesets);
        Assert.IsType<AlwaysTrueRuleset>(nestedRule.Rulesets[0]);
        Assert.Single(nestedRule.Actions);
        Assert.IsType<SendCommandAction>(nestedRule.Actions[0]);
    }

    [Fact]
    [Trait("Category", "EventRuleUnknown")]
    public void EventRuleUnknown_UnknownTriggerDiscriminator_ThrowsExplicitFailure()
    {
        var ex = Assert.Throws<JsonSerializationException>(() => DeserializeEventRuleFixture("unknown-trigger-discriminator-event-rule.json"));

        Assert.Contains("Unknown TriggerDefinition discriminator 'FutureTrigger'", ex.Message);
        Assert.Contains("Known values: ConsoleOutput, Schedule, InstanceStatus", ex.Message);
    }

    [Fact]
    [Trait("Category", "EventRuleUnknown")]
    public void EventRuleUnknown_MissingRulesetDiscriminator_ThrowsExplicitFailure()
    {
        var ex = Assert.Throws<JsonSerializationException>(() => DeserializeEventRuleFixture("missing-ruleset-discriminator-event-rule.json"));

        Assert.Contains("Missing discriminator 'type' for RulesetDefinition", ex.Message);
    }

    [Fact]
    [Trait("Category", "EventRuleUnknown")]
    public void EventRuleUnknown_InvalidActionDiscriminatorType_ThrowsExplicitFailure()
    {
        var ex = Assert.Throws<JsonSerializationException>(() => DeserializeEventRuleFixture("invalid-action-discriminator-event-rule.json"));

        Assert.Contains("Invalid discriminator 'type' for ActionDefinition: expected string", ex.Message);
    }

    private static EventRule DeserializeEventRuleFixture(string fixtureFile)
    {
        var json = File.ReadAllText(Path.Combine(PersistenceFixturePaths.EventRuleDir, fixtureFile));
        return JsonConvert.DeserializeObject<EventRule>(json, JsonSettings.Settings)!;
    }
}
