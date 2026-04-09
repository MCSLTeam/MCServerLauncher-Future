using System;
using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.DaemonClient.Serialization;
using Xunit;

namespace MCServerLauncher.ProtocolTests;

/// <summary>
/// Verify STJ foundation types compile and basic serialization works.
/// </summary>
public class StjFoundationTests
{
    [Fact]
    public void StjResolver_CreateDefaultResolver_ReturnsNonNull()
    {
        var resolver = StjResolver.CreateDefaultResolver();
        Assert.NotNull(resolver);
    }

    [Fact]
    public void StjResolver_CreateDefaultOptions_ReturnsConfiguredOptions()
    {
        var options = StjResolver.CreateDefaultOptions();
        Assert.NotNull(options);
        Assert.NotNull(options.TypeInfoResolver);
        Assert.Equal(3, options.Converters.Count);
    }

    [Fact]
    public void GuidStjConverter_SerializesGuid()
    {
        var guid = Guid.NewGuid();
        var options = StjResolver.CreateDefaultOptions();
        var json = JsonSerializer.Serialize(guid, options);
        Assert.Contains(guid.ToString(), json);
    }

    [Fact]
    public void GuidStjConverter_DeserializesGuidDictionaryKeys()
    {
        var guid = Guid.Parse("fdbf680c-fe52-4f1d-89ba-a0d9d8b857b3");
        var json =
            $$"""
              {
                "reports": {
                  "{{guid}}": {
                    "status": "stopped",
                    "config": {
                      "name": "demo",
                      "target": "server.jar",
                      "instance_type": "mc_java",
                      "target_type": "jar",
                      "mc_version": "1.21.1",
                      "input_encoding": "utf-8",
                      "output_encoding": "utf-8",
                      "java_path": "java",
                      "arguments": [],
                      "env": {},
                      "event_rules": []
                    },
                    "properties": {},
                    "players": [],
                    "performance_counter": {
                      "cpu": 0,
                      "memory": 0
                    }
                  }
                }
              }
            """;

        var result = JsonSerializer.Deserialize<GetAllReportsResult>(json, DaemonClientRpcJsonBoundary.StjOptions);

        Assert.NotNull(result);
        Assert.True(result!.Reports.ContainsKey(guid));
    }

    [Fact]
    public void PlaceHolderStringStjConverter_SerializesPattern()
    {
        var phs = new PlaceHolderString("{test}");
        var options = StjResolver.CreateDefaultOptions();
        var json = JsonSerializer.Serialize(phs, options);
        Assert.Contains("{test}", json);
    }
}
