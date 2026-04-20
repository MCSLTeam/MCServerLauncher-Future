using System;
using System.Collections.Generic;
using SysTextJsonPropertyName = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace MCServerLauncher.Common.ProtoType.Action
{
    public sealed record GetEventRulesParameter : IActionParameter
    {
        [SysTextJsonPropertyName("instance_id")]
        public Guid InstanceId { get; set; }
    }

    public sealed record SaveEventRulesParameter : IActionParameter
    {
        [SysTextJsonPropertyName("instance_id")]
        public Guid InstanceId { get; set; }

        [SysTextJsonPropertyName("rules")]
        public List<EventTrigger.EventRule> Rules { get; set; } = new();
    }
}
