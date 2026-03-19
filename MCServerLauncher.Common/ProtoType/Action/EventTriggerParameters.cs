using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace MCServerLauncher.Common.ProtoType.Action
{
    public sealed record GetEventRulesParameter : IActionParameter
    {
        [JsonProperty("instance_id")]
        public Guid InstanceId { get; set; }
    }

    public sealed record SaveEventRulesParameter : IActionParameter
    {
        [JsonProperty("instance_id")]
        public Guid InstanceId { get; set; }

        [JsonProperty("rules")]
        public List<EventTrigger.EventRule> Rules { get; set; } = new();
    }
}
