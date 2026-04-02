using System;
using System.Text.Json;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Serialization;
using Xunit;

namespace MCServerLauncher.ProtocolTests;

/// <summary>
/// T4: Verify STJ foundation types compile and basic serialization works
/// </summary>
public class T4StjFoundationTests
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
    public void PlaceHolderStringStjConverter_SerializesPattern()
    {
        var phs = new PlaceHolderString("{test}");
        var options = StjResolver.CreateDefaultOptions();
        var json = JsonSerializer.Serialize(phs, options);
        Assert.Contains("{test}", json);
    }
}
