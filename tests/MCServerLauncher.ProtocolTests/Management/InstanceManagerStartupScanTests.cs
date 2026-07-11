using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Storage;

namespace MCServerLauncher.ProtocolTests;

public sealed class InstanceManagerStartupScanTests
{
    [Fact]
    public void Create_WhenEnumeratedDirectoryDisappears_SkipsItAndLoadsOtherInstances()
    {
        var disappearedDirectory = Path.Combine(FileManager.InstancesRoot, Guid.NewGuid().ToString());
        var config = CreateConfig();
        var validDirectory = config.GetWorkingDirectory();
        Directory.CreateDirectory(disappearedDirectory);
        Directory.CreateDirectory(validDirectory);
        FileManager.WriteJsonAndBackup(Path.Combine(validDirectory, InstanceConfig.FileName), config);
        Directory.Delete(disappearedDirectory);

        try
        {
            var manager = Assert.IsType<InstanceManager>(
                InstanceManager.Create([disappearedDirectory, validDirectory]));

            Assert.True(manager.Instances.TryGetValue(config.Uuid, out var loaded));
            Assert.Equal(config.Name, loaded.Config.Name);
        }
        finally
        {
            if (Directory.Exists(validDirectory))
                Directory.Delete(validDirectory, true);
        }
    }

    private static InstanceConfig CreateConfig()
    {
        return new InstanceConfig
        {
            Uuid = Guid.NewGuid(),
            Name = "startup-scan-test",
            Target = "server.jar",
            TargetType = TargetType.Jar,
            InstanceType = InstanceType.MCJava,
            JavaPath = "java",
            Arguments = ["nogui"]
        };
    }
}
