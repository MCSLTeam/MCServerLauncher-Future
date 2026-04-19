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

    [Fact]
    public void CommonLibrary_IsReferenced()
    {
        // Verify the Common library reference is functional
        var commonAssembly = typeof(MCServerLauncher.Common.ProtoType.Action.ActionRequest).Assembly;
        Assert.NotNull(commonAssembly);
    }
}
