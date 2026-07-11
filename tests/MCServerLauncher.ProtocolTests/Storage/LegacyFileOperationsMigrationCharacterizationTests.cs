using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Daemon.Storage;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests;

public class LegacyFileOperationsMigrationCharacterizationTests
{
    [Fact]
    [Trait("Category", "LegacyFileOperationsMigration")]
    public async Task FileApplicationOperations_RepresentativeSuccessChain_PreservesInfoAndContent()
    {
        var fixture = CreateFixtureRoot();
        var coordinator = new FileSessionCoordinator();
        try
        {
            var sourceDirectory = Path.Combine(fixture.RelativePath, "source");
            var originalFile = Path.Combine(sourceDirectory, "original.txt");
            const string content = "file-application-operations";

            AssertOk(await coordinator.CreateDirectoryAsync(new PathRequest(sourceDirectory), CancellationToken.None));
            await File.WriteAllTextAsync(FileManager.ResolveAndValidatePath(originalFile), content);

            var rootInfo = AssertOk(await coordinator.GetDirectoryInfoAsync(new PathRequest(fixture.RelativePath), CancellationToken.None));
            Assert.Contains(rootInfo.Directories, directory => directory.Name == "source");

            var originalInfo = AssertOk(await coordinator.GetFileInfoAsync(new PathRequest(originalFile), CancellationToken.None));
            Assert.Equal(content.Length, originalInfo.Meta.Size);

            AssertOk(await coordinator.RenameFileAsync(new PathRenameRequest(originalFile, "renamed.txt"), CancellationToken.None));
            var renamedFile = Path.Combine(sourceDirectory, "renamed.txt");
            var targetDirectory = Path.Combine(fixture.RelativePath, "target");
            AssertOk(await coordinator.CreateDirectoryAsync(new PathRequest(targetDirectory), CancellationToken.None));

            var movedFile = Path.Combine(targetDirectory, "moved.txt");
            AssertOk(await coordinator.MoveFileAsync(new PathTransferRequest(renamedFile, movedFile), CancellationToken.None));
            var copiedFile = Path.Combine(targetDirectory, "copied.txt");
            AssertOk(await coordinator.CopyFileAsync(new PathTransferRequest(movedFile, copiedFile), CancellationToken.None));
            Assert.Equal(content, await File.ReadAllTextAsync(FileManager.ResolveAndValidatePath(copiedFile)));

            AssertOk(await coordinator.DeleteFileAsync(new PathRequest(copiedFile), CancellationToken.None));
            AssertOk(await coordinator.DeleteDirectoryAsync(new DeleteDirectoryRequest(fixture.RelativePath, Recursive: true), CancellationToken.None));
            Assert.False(Directory.Exists(fixture.ResolvedPath));
        }
        finally
        {
            CleanupFixtureRoot(fixture.ResolvedPath);
        }
    }

    [Fact]
    [Trait("Category", "LegacyFileOperationsMigration")]
    public async Task FileApplicationOperations_OutOfRootPaths_AreRejectedBeforeFileWork()
    {
        var fixture = CreateFixtureRoot();
        var coordinator = new FileSessionCoordinator();
        try
        {
            var result = await coordinator.CreateDirectoryAsync(new PathRequest(".."), CancellationToken.None);
            Assert.True(result.IsErr(out _));
            var error = result.UnwrapErr();
            Assert.Equal("file.path.invalid", error.Code);
        }
        finally
        {
            CleanupFixtureRoot(fixture.ResolvedPath);
        }
    }

    private static T AssertOk<T>(Result<T, MCServerLauncher.Daemon.API.Errors.DaemonError> result)
        where T : notnull
    {
        if (result.IsOk(out var value))
            return value;

        throw new Xunit.Sdk.XunitException(result.UnwrapErr().Message);
    }

    private static FixtureRoot CreateFixtureRoot()
    {
        var relativePath = Path.Combine("caches", $"file-operations-migration-{Guid.NewGuid():N}");
        var resolvedPath = FileManager.ResolveAndValidatePath(relativePath);
        return new FixtureRoot(relativePath, resolvedPath);
    }

    private static void CleanupFixtureRoot(string resolvedPath)
    {
        if (Directory.Exists(resolvedPath))
            Directory.Delete(resolvedPath, recursive: true);
    }

    private readonly record struct FixtureRoot(string RelativePath, string ResolvedPath);
}
