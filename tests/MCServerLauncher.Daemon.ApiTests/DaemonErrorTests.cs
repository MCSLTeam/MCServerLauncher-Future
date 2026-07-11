using System.Reflection;
using System.Text.Json;
using MCServerLauncher.Daemon.API.Errors;

namespace MCServerLauncher.Daemon.ApiTests;

public sealed class DaemonErrorTests
{
    [Fact]
    public void DetailsAreDetachedFromTheSourceDocument()
    {
        ValidationDaemonError error;

        using (var document = JsonDocument.Parse("{\"field\":\"name\"}"))
        {
            error = new ValidationDaemonError("instance.invalid", "The instance is invalid.", document.RootElement);
        }

        Assert.Equal("name", error.Details!.Value.GetProperty("field").GetString());
        Assert.Equal(DaemonErrorKind.Validation, error.Kind);
        Assert.Equal("instance.invalid", error.Code);
    }

    [Fact]
    public void HierarchyIsClosedToTheKnownSdkErrorTypes()
    {
        var errorTypes = typeof(DaemonError).Assembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && type.IsSubclassOf(typeof(DaemonError)))
            .ToArray();

        Assert.All(errorTypes, type => Assert.True(type.IsSealed));
        Assert.Equal(
            [
                typeof(ConflictDaemonError),
                typeof(InternalDaemonError),
                typeof(NotFoundDaemonError),
                typeof(PermissionDaemonError),
                typeof(StorageDaemonError),
                typeof(TransportDaemonError),
                typeof(ValidationDaemonError)
            ],
            errorTypes.OrderBy(type => type.Name));
    }

    [Fact]
    public void BaseConstructorIsNotPublicOrProtected()
    {
        var constructor = typeof(DaemonError).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();

        Assert.True(constructor.IsFamilyAndAssembly);
    }
}
