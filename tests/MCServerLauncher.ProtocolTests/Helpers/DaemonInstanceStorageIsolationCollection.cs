using MCServerLauncher.Daemon.Storage;

namespace MCServerLauncher.ProtocolTests.Helpers;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DaemonInstanceStorageIsolationCollection : ICollectionFixture<DaemonInstanceStorageFixture>
{
    public const string Name = "DaemonInstanceStorageIsolation";
}

public sealed class DaemonInstanceStorageFixture : IAsyncLifetime
{
    public Task InitializeAsync()
    {
        ResetStorage();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        ResetStorage();
        return Task.CompletedTask;
    }

    private static void ResetStorage()
    {
        if (Directory.Exists(FileManager.Root))
            Directory.Delete(FileManager.Root, recursive: true);

        Directory.CreateDirectory(FileManager.Root);
    }
}
