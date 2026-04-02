using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using SysTextJsonPropertyName = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace MCServerLauncher.Common.ProtoType.Action
{
    public sealed record GetEventRulesParameter : IActionParameter
    {
        [JsonProperty("instance_id")]
        [SysTextJsonPropertyName("instance_id")]
        public Guid InstanceId { get; set; }
    }

    public sealed record SaveEventRulesParameter : IActionParameter
    {
        [JsonProperty("instance_id")]
        [SysTextJsonPropertyName("instance_id")]
        public Guid InstanceId { get; set; }

        [JsonProperty("rules")]
        [SysTextJsonPropertyName("rules")]
        public List<EventTrigger.EventRule> Rules { get; set; } = new();
    }
}
