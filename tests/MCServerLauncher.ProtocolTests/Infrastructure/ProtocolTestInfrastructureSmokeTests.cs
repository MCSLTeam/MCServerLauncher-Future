using MCServerLauncher.Common.ProtoType.Serialization;

namespace MCServerLauncher.ProtocolTests;

public class ProtocolTestInfrastructureSmokeTests
{
    [Fact]
    public void ProtocolTestInfrastructure_IsOperational()
    {
        // Verify test infrastructure can resolve a boundary type from Common
        var resolver = StjResolver.CreateDefaultResolver();
        Assert.NotNull(resolver);
    }

}
