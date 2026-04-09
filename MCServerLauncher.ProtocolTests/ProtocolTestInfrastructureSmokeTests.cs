namespace MCServerLauncher.ProtocolTests;

public class ProtocolTestInfrastructureSmokeTests
{
    [Fact]
    public void ProtocolTestInfrastructure_IsOperational()
    {
        // Seed test to verify test infrastructure is operational
        // This test confirms the test project is properly wired to the solution
        Assert.True(true);
    }

    [Fact]
    public void CommonLibrary_IsReferenced()
    {
        // Verify the Common library reference is functional
        var commonAssembly = typeof(MCServerLauncher.Common.ProtoType.Action.ActionRequest).Assembly;
        Assert.NotNull(commonAssembly);
    }
}
