using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Common.ProtoType.Instance;
using ContractInstanceReport = MCServerLauncher.Common.Contracts.Instances.InstanceReport;

namespace MCServerLauncher.Daemon.ApiTests;

public sealed class SharedContractTests
{
    [Fact]
    public void ApplicationRequestAndResultDtosAreOwnedByCommon()
    {
        var commonAssembly = typeof(CreateInstanceRequest).Assembly;
        var representativeTypes = new[]
        {
            typeof(CreateInstanceRequest),
            typeof(InstanceSettingsResult),
            typeof(PathRequest),
            typeof(DownloadChunk),
            typeof(SystemInfo),
            typeof(JavaRuntimeList),
            typeof(EventRuleSet)
        };

        Assert.All(representativeTypes, type => Assert.Same(commonAssembly, type.Assembly));
    }

    [Fact]
    public void JsonElementContractsDetachFromTheSourceDocumentAndHaveNoSetter()
    {
        InstanceConfiguration configuration;
        EventRuleSet ruleSet;
        EventRuleUpdateRequest updateRequest;

        using (var document = JsonDocument.Parse("{\"value\":42}"))
        {
            configuration = new InstanceConfiguration(
                Guid.NewGuid(),
                "example",
                "server.jar",
                InstanceType.MCJava,
                TargetType.Jar,
                "1.21.5",
                "utf-8",
                "utf-8",
                "java",
                ImmutableArray<string>.Empty,
                ImmutableDictionary<string, string>.Empty,
                document.RootElement);
            ruleSet = new EventRuleSet(Guid.NewGuid(), document.RootElement);
            updateRequest = new EventRuleUpdateRequest(Guid.NewGuid(), document.RootElement);
        }

        Assert.Equal(42, configuration.EventRules.GetProperty("value").GetInt32());
        Assert.Equal(42, ruleSet.Rules.GetProperty("value").GetInt32());
        Assert.Equal(42, updateRequest.Rules.GetProperty("value").GetInt32());

        Assert.Null(typeof(InstanceConfiguration).GetProperty(nameof(InstanceConfiguration.EventRules))!.SetMethod);
        Assert.Null(typeof(EventRuleSet).GetProperty(nameof(EventRuleSet.Rules))!.SetMethod);
        Assert.Null(typeof(EventRuleUpdateRequest).GetProperty(nameof(EventRuleUpdateRequest.Rules))!.SetMethod);
    }

    [Fact]
    public void SharedContractsDoNotExposeMutableCollections()
    {
        var contractTypes = typeof(CreateInstanceRequest).Assembly.GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("MCServerLauncher.Common.Contracts", StringComparison.Ordinal) == true);

        foreach (var property in contractTypes.SelectMany(type =>
                     type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)))
        {
            if (IsEventRulePersistenceProperty(property))
            {
                continue;
            }

            Assert.False(
                property.PropertyType.IsGenericType &&
                property.PropertyType.GetGenericTypeDefinition() is var definition &&
                (definition == typeof(List<>) || definition == typeof(Dictionary<,>) || definition == typeof(HashSet<>)),
                $"Mutable collection property: {property.DeclaringType}.{property.Name}");
            Assert.False(property.PropertyType.IsArray, $"Array property: {property.DeclaringType}.{property.Name}");
        }
    }

    [Fact]
    public void ParityContractsExposeCompleteFileSystemAndRuntimeFacts()
    {
        Assert.Equal(
            new[] { "Parent", "Files", "Directories" },
            typeof(DirectoryDetails).GetProperties().Select(property => property.Name).ToArray());

        Assert.Equal(
            new[] { "Os", "Cpu", "Mem", "Drive", "Drives", "DaemonVersion" },
            typeof(SystemInfo).GetProperties().Select(property => property.Name).ToArray());

        Assert.NotNull(typeof(JavaRuntime).GetProperty(nameof(JavaRuntime.Architecture)));
        Assert.Null(typeof(JavaRuntime).GetProperty("Vendor"));

        var reportProperties = typeof(ContractInstanceReport).GetProperties().Select(property => property.Name).ToHashSet();
        Assert.Contains(nameof(ContractInstanceReport.Config), reportProperties);
        Assert.Contains(nameof(ContractInstanceReport.Properties), reportProperties);
        Assert.Contains(nameof(ContractInstanceReport.Players), reportProperties);
        Assert.Contains(nameof(ContractInstanceReport.PerformanceCounter), reportProperties);
        Assert.Contains(nameof(ContractInstanceReport.ProcessId), reportProperties);
    }

    [Fact]
    public void EventRulePersistenceModelsKeepTheirExplicitMutableAuthoringShape()
    {
        Assert.Equal(typeof(List<TriggerDefinition>), typeof(EventRule).GetProperty(nameof(EventRule.Triggers))!.PropertyType);
        Assert.Equal(typeof(List<RulesetDefinition>), typeof(EventRule).GetProperty(nameof(EventRule.Rulesets))!.PropertyType);
        Assert.Equal(typeof(List<ActionDefinition>), typeof(EventRule).GetProperty(nameof(EventRule.Actions))!.PropertyType);
        Assert.Contains(
            typeof(EventRule).GetProperties(),
            property => property.SetMethod is not null && property.Name == nameof(EventRule.Name));
    }

    private static bool IsEventRulePersistenceProperty(PropertyInfo property) =>
        property.DeclaringType == typeof(EventRule) ||
        (property.DeclaringType is not null &&
         (typeof(RulesetDefinition).IsAssignableFrom(property.DeclaringType) ||
          typeof(TriggerDefinition).IsAssignableFrom(property.DeclaringType) ||
          typeof(ActionDefinition).IsAssignableFrom(property.DeclaringType)));
}
