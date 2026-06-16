using System;
using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Common.Network;
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
        Assert.All(new[] { typeof(GuidStjConverter), typeof(EncodingStjConverter), typeof(PlaceHolderStringStjConverter) },
                   t => Assert.Contains(options.Converters, c => c.GetType() == t));
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
    public void InstanceConfig_EncodingWebNames_RoundTripAsStableStrings()
    {
        var config = new InstanceConfig
        {
            Name = "demo",
            Target = "server.jar",
            InstanceType = InstanceType.MCJava,
            TargetType = TargetType.Jar,
            InputEncoding = System.Text.Encoding.UTF8,
            OutputEncoding = System.Text.Encoding.Unicode
        };

        var json = JsonSerializer.Serialize(config, PersistenceContext.Default.InstanceConfig);
        var parsed = JsonSerializer.Deserialize(json, PersistenceContext.Default.InstanceConfig);

        Assert.Contains("\"input_encoding\":\"utf-8\"", json);
        Assert.Contains("\"output_encoding\":\"utf-16\"", json);
        Assert.Equal(System.Text.Encoding.UTF8.WebName, parsed!.InputEncoding.WebName);
        Assert.Equal(System.Text.Encoding.Unicode.WebName, parsed.OutputEncoding.WebName);
    }

    [Fact]
    public void PlaceHolderStringStjConverter_SerializesPattern()
    {
        var phs = new PlaceHolderString("{test}");
        var options = StjResolver.CreateDefaultOptions();
        var json = JsonSerializer.Serialize(phs, options);
        Assert.Contains("{test}", json);
    }

    [Theory]
    [InlineData(-1, -1, 0, 0)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(50.25, 1024, 50.25, 1024)]
    [InlineData(120, 2048, 100, 2048)]
    [InlineData(double.NaN, 4096, 0, 4096)]
    [InlineData(double.PositiveInfinity, 8192, 0, 8192)]
    [InlineData(double.NegativeInfinity, 16384, 0, 16384)]
    public void InstancePerformanceCounter_NormalizesInvalidCpuAndMemory(
        double cpu,
        long memory,
        double expectedCpu,
        long expectedMemory)
    {
        var counter = new InstancePerformanceCounter(cpu, memory);

        Assert.Equal(expectedCpu, counter.Cpu);
        Assert.Equal(expectedMemory, counter.Memory);
    }

    [Fact]
    public void InstancePerformanceCounter_SourceGeneratedJson_NormalizesInvalidValues()
    {
        const string json = """{"cpu":-5,"memory":-1024}""";

        var counter = JsonSerializer.Deserialize(json, ActionResultsContext.Default.InstancePerformanceCounter);

        Assert.Equal(0, counter.Cpu);
        Assert.Equal(0, counter.Memory);
    }

    [Fact]
    public void SlpPayload_ReflectionDisabled_DeserializesWithSourceGeneratedMetadata()
    {
        const string key = "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault";
        var hadPrevious = AppContext.TryGetSwitch(key, out var previous);

        try
        {
            AppContext.SetSwitch(key, false);

            const string json = """
                {
                  "version": {
                    "name": "1.20.4",
                    "protocol": 765
                  },
                  "players": {
                    "max": 20,
                    "online": 1,
                    "sample": [
                      {
                        "name": "Steve",
                        "id": "8667ba71-b85a-4004-af54-457a9734eed7"
                      }
                    ]
                  },
                  "description": {
                    "text": "A Minecraft Server"
                  },
                  "favicon": "data:image/png;base64,abc"
                }
                """;

            var payload = JsonSerializer.Deserialize(json, SlpJsonContext.Default.PingPayload);

            Assert.NotNull(payload);
            Assert.Equal("1.20.4", payload!.Version.Name);
            Assert.Equal(765, payload.Version.Protocol);
            Assert.Equal(1, payload.Players.Online);
            Assert.Equal("Steve", Assert.Single(payload.Players.Sample).Name);
            Assert.Equal("A Minecraft Server", payload.Description);
            Assert.Equal("data:image/png;base64,abc", payload.Icon);
        }
        finally
        {
            if (hadPrevious)
                AppContext.SetSwitch(key, previous);
            else
                AppContext.SetData(key, null);
        }
    }
}
