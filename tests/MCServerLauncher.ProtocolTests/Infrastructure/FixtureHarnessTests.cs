using System.Text.Json;
using MCServerLauncher.ProtocolTests.Fixtures.ConverterParity;
using MCServerLauncher.ProtocolTests.Fixtures.Persistence;
using MCServerLauncher.ProtocolTests.Fixtures.Rpc;
using MCServerLauncher.ProtocolTests.Helpers;
using MCServerLauncher.ProtocolTests.Helpers.Integration;

namespace MCServerLauncher.ProtocolTests;

public class FixtureHarnessTests
{
    [Fact]
    [Trait("Category", "FixtureHarness")]
    public void FixtureRoot_Directories_Exist()
    {
        // Validate that all fixture root directories are properly defined
        Assert.True(Directory.Exists(RpcFixturePaths.FixtureRoot), "RPC fixture root should exist");
        Assert.True(Directory.Exists(PersistenceFixturePaths.FixtureRoot), "Persistence fixture root should exist");
        Assert.True(Directory.Exists(ConverterParityFixturePaths.FixtureRoot), "ConverterParity fixture root should exist");
        Assert.True(Directory.Exists(IntegrationHelperPaths.HelperRoot), "Integration helper root should exist");
    }

    [Fact]
    [Trait("Category", "FixtureHarness")]
    public void RpcFixtureSubDirectories_Exist()
    {
        Assert.True(Directory.Exists(RpcFixturePaths.ActionRequestDir), "ActionRequest fixture dir should exist");
        Assert.True(Directory.Exists(RpcFixturePaths.ActionResponseDir), "ActionResponse fixture dir should exist");
        Assert.True(Directory.Exists(RpcFixturePaths.EventPacketDir), "EventPacket fixture dir should exist");
    }

    [Fact]
    [Trait("Category", "FixtureHarness")]
    public void PersistenceFixtureSubDirectories_Exist()
    {
        Assert.True(Directory.Exists(PersistenceFixturePaths.InstanceConfigDir), "InstanceConfig fixture dir should exist");
        Assert.True(Directory.Exists(PersistenceFixturePaths.EventRuleDir), "EventRule fixture dir should exist");
        Assert.True(Directory.Exists(PersistenceFixturePaths.ConfigDir), "Config fixture dir should exist");
    }

    [Fact]
    [Trait("Category", "FixtureHarness")]
    public void ConverterParitySubDirectories_Exist()
    {
        Assert.True(Directory.Exists(ConverterParityFixturePaths.GuidDir), "Guid fixture dir should exist");
        Assert.True(Directory.Exists(ConverterParityFixturePaths.EncodingDir), "Encoding fixture dir should exist");
        Assert.True(Directory.Exists(ConverterParityFixturePaths.PlaceHolderStringDir), "PlaceHolderString fixture dir should exist");
        Assert.True(Directory.Exists(ConverterParityFixturePaths.PermissionDir), "Permission fixture dir should exist");
        Assert.True(Directory.Exists(ConverterParityFixturePaths.EnumDir), "Enum fixture dir should exist");
    }

    [Fact]
    [Trait("Category", "FixtureHarness")]
    public void FixtureHarness_LoadFixture_LoadsValidJson()
    {
        // Create a temp JSON fixture to test loading
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_fixture_{Guid.NewGuid()}.json");
        var expectedJson = """{"action": "test", "id": "550e8400-e29b-41d4-a716-446655440000"}""";
        File.WriteAllText(tempPath, expectedJson);

        try
        {
            var element = FixtureHarness.LoadFixture(tempPath);
            Assert.Equal(JsonValueKind.Object, element.ValueKind);
            Assert.Equal("test", element.GetProperty("action").GetString());
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    [Trait("Category", "FixtureHarness")]
    public void FixtureHarness_LoadFixture_ThrowsOnMissingFile()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "nonexistent_fixture_12345.json");
        Assert.Throws<FileNotFoundException>(() => FixtureHarness.LoadFixture(nonExistentPath));
    }

    [Fact]
    [Trait("Category", "FixtureHarness")]
    public void FixtureHarness_StructuralEquals_ReturnsTrueForEqualJson()
    {
        var json1 = """{"action": "start", "id": "550e8400-e29b-41d4-a716-446655440000"}""";
        var json2 = """{"action": "start", "id": "550e8400-e29b-41d4-a716-446655440000"}""";

        var element1 = FixtureHarness.ParseJson(json1);
        var element2 = FixtureHarness.ParseJson(json2);

        Assert.True(FixtureHarness.StructuralEquals(element1, element2));
    }

    [Fact]
    [Trait("Category", "FixtureHarness")]
    public void FixtureHarness_StructuralEquals_ReturnsFalseForDifferentJson()
    {
        var json1 = """{"action": "start"}""";
        var json2 = """{"action": "stop"}""";

        var element1 = FixtureHarness.ParseJson(json1);
        var element2 = FixtureHarness.ParseJson(json2);

        Assert.False(FixtureHarness.StructuralEquals(element1, element2));
    }

    [Fact]
    [Trait("Category", "FixtureHarness")]
    public void FixtureHarness_StructuralEquals_StringOverload_Works()
    {
        var json1 = """{"action": "test"}""";
        var json2 = """{"action": "test"}""";

        Assert.True(FixtureHarness.StructuralEquals(json1, json2));
    }

    [Fact]
    [Trait("Category", "FixtureHarness")]
    public void FixtureHarness_AssertStructuralEquals_ThrowsOnMismatch()
    {
        var json1 = """{"action": "start"}""";
        var json2 = """{"action": "stop"}""";

        var element1 = FixtureHarness.ParseJson(json1);
        var element2 = FixtureHarness.ParseJson(json2);

        var ex = Assert.Throws<AssertEqualityException>(() =>
            FixtureHarness.AssertStructuralEquals(element1, element2, "Action mismatch"));
        Assert.Contains("Action mismatch", ex.Message);
    }

    [Fact]
    [Trait("Category", "FixtureHarness")]
    public void FixtureHarness_AssertStructuralEquals_PassesOnEqual()
    {
        var json1 = """{"action": "start", "id": "123"}""";
        var json2 = """{"action": "start", "id": "123"}""";

        var element1 = FixtureHarness.ParseJson(json1);
        var element2 = FixtureHarness.ParseJson(json2);

        // Should not throw
        FixtureHarness.AssertStructuralEquals(element1, element2);
    }

    [Fact]
    [Trait("Category", "FixtureHarness")]
    public void FixtureHarness_SerializeCanonical_ProducesDeterministicOutput()
    {
        var obj = new { Action = "start", Id = Guid.Parse("550e8400-e29b-41d4-a716-446655440000") };
        var serialized = FixtureHarness.SerializeCanonical(obj);

        // Should be a single-line JSON without extra whitespace
        Assert.DoesNotContain("\n", serialized);
        Assert.DoesNotContain("\r", serialized);
        Assert.Contains("\"Action\":\"start\"", serialized);
    }
}