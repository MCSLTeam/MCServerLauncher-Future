using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.ProtocolDocs;

internal static class ApifoxProjectGenerator
{
    public static byte[] Generate() =>
        MCServerLauncher.Daemon.API.Protocol.ApifoxProjectGenerator.GenerateBuiltIn();
}
