using System.Text.Json;

namespace MCServerLauncher.Common.Contracts.EventRules;

public sealed record EventRuleQuery(Guid InstanceId);

public sealed record EventRuleSet
{
    public EventRuleSet(Guid instanceId, JsonElement rules)
    {
        InstanceId = instanceId;
        Rules = rules.Clone();
    }

    public Guid InstanceId { get; }

    public JsonElement Rules { get; }
}

public sealed record EventRuleUpdateRequest
{
    public EventRuleUpdateRequest(Guid instanceId, JsonElement rules)
    {
        InstanceId = instanceId;
        Rules = rules.Clone();
    }

    public Guid InstanceId { get; }

    public JsonElement Rules { get; }
}
