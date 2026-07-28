using System.Reflection;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApiTests;

public sealed class ApplicationContractTests
{
    [Fact]
    public void DaemonApplicationComposesEveryDomainService()
    {
        // SDK-4b: the aggregate root carries the Preview-2 domains too, so a remote client reaches
        // the same surface a local caller does.
        var properties = typeof(IDaemonApplication).GetProperties();

        Assert.Collection(
            properties.OrderBy(property => property.Name, StringComparer.Ordinal),
            property => Assert.Equal(typeof(IAuditApplication), property.PropertyType),
            property => Assert.Equal(typeof(IAutomationApplication), property.PropertyType),
            property => Assert.Equal(typeof(IBackupApplication), property.PropertyType),
            property => Assert.Equal(typeof(IEventRuleApplication), property.PropertyType),
            property => Assert.Equal(typeof(IFileApplication), property.PropertyType),
            property => Assert.Equal(typeof(IInstanceApplication), property.PropertyType),
            property => Assert.Equal(typeof(IMonitoringApplication), property.PropertyType),
            property => Assert.Equal(typeof(IOperationApplication), property.PropertyType),
            property => Assert.Equal(typeof(IProvisioningApplication), property.PropertyType),
            property => Assert.Equal(typeof(ISystemApplication), property.PropertyType));
    }

    [Fact]
    public void ApplicationMethodsUseResultAndEndWithCancellationToken()
    {
        var applicationInterfaces = new[]
        {
            typeof(IInstanceApplication),
            typeof(IFileApplication),
            typeof(ISystemApplication),
            typeof(IEventRuleApplication),
            typeof(IOperationApplication),
            typeof(IProvisioningApplication),
            typeof(IBackupApplication),
            typeof(IAuditApplication),
            typeof(IMonitoringApplication),
            typeof(IAutomationApplication)
        };

        foreach (var method in applicationInterfaces.SelectMany(type => type.GetMethods()))
        {
            Assert.Equal(typeof(CancellationToken), method.GetParameters().Last().ParameterType);
            Assert.True(method.ReturnType.IsGenericType);
            Assert.Equal(typeof(Task<>), method.ReturnType.GetGenericTypeDefinition());

            var result = method.ReturnType.GetGenericArguments()[0];
            Assert.True(result.IsGenericType);
            Assert.Equal("Result`2", result.Name);
            Assert.Equal(typeof(DaemonError), result.GetGenericArguments()[1]);
        }
    }

    [Fact]
    public void FileApplicationDoesNotExposeDisposableOrStreamHandles()
    {
        var exposedTypes = InterfaceMethods(typeof(IFileApplication))
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                .Append(UnwrapAwaitableResult(method.ReturnType)))
            .SelectMany(UnwrapGenericArguments)
            .ToArray();

        Assert.NotEmpty(exposedTypes);

        Assert.DoesNotContain(exposedTypes, type => typeof(IDisposable).IsAssignableFrom(type));
        Assert.DoesNotContain(exposedTypes, type => typeof(Stream).IsAssignableFrom(type));
    }

    [Fact]
    public void FileApplicationPreservesIndependentFileAndDirectoryOperations()
    {
        // file.read and file.write split the surface into two narrow views, so the composed
        // interface declares nothing of its own; the operations live one level up the hierarchy.
        var methods = InterfaceMethods(typeof(IFileApplication)).ToDictionary(method => method.Name);

        Assert.Contains(typeof(IFileReadApplication), typeof(IFileApplication).GetInterfaces());
        Assert.Contains(typeof(IFileWriteApplication), typeof(IFileApplication).GetInterfaces());

        AssertResultType<DirectoryDetails>(methods[nameof(IFileApplication.GetDirectoryInfoAsync)]);
        AssertResultType<FileDetails>(methods[nameof(IFileApplication.GetFileInfoAsync)]);
        AssertResultType<Unit>(methods[nameof(IFileApplication.CreateDirectoryAsync)]);
        AssertResultType<Unit>(methods[nameof(IFileApplication.DeleteFileAsync)]);
        AssertResultType<Unit>(methods[nameof(IFileApplication.DeleteDirectoryAsync)]);
        AssertResultType<Unit>(methods[nameof(IFileApplication.RenameFileAsync)]);
        AssertResultType<Unit>(methods[nameof(IFileApplication.RenameDirectoryAsync)]);
        AssertResultType<Unit>(methods[nameof(IFileApplication.MoveFileAsync)]);
        AssertResultType<Unit>(methods[nameof(IFileApplication.MoveDirectoryAsync)]);
        AssertResultType<Unit>(methods[nameof(IFileApplication.CopyFileAsync)]);
        AssertResultType<Unit>(methods[nameof(IFileApplication.CopyDirectoryAsync)]);

        Assert.Equal(
            typeof(DeleteDirectoryRequest),
            methods[nameof(IFileApplication.DeleteDirectoryAsync)].GetParameters()[0].ParameterType);

        // Byte transfer stays reachable through the composed surface after the split.
        AssertResultType<DownloadSession>(methods[nameof(IFileApplication.OpenDownloadAsync)]);
        AssertResultType<UploadSession>(methods[nameof(IFileApplication.OpenUploadAsync)]);
    }

    /// <summary>
    /// Interface reflection does not inherit: GetMethods on a composed interface returns only its
    /// own declarations, so the narrow views have to be walked explicitly.
    /// </summary>
    private static IEnumerable<MethodInfo> InterfaceMethods(Type type) =>
        type.GetMethods().Concat(type.GetInterfaces().SelectMany(inherited => inherited.GetMethods()));

    [Fact]
    public void InstanceApplicationReturnsParityCompleteCreateAndSettingsResults()
    {
        var methods = typeof(IInstanceApplication)
            .GetInterfaces()
            .Append(typeof(IInstanceApplication))
            .SelectMany(static contract => contract.GetMethods())
            .ToDictionary(method => method.Name);

        AssertResultType<CreateInstanceResult>(methods[nameof(IInstanceApplication.CreateInstanceAsync)]);
        AssertResultType<InstanceSettingsResult>(methods[nameof(IInstanceApplication.GetInstanceSettingsAsync)]);
        AssertResultType<UpdateInstanceSettingsResult>(methods[nameof(IInstanceApplication.UpdateInstanceSettingsAsync)]);
    }

    private static Type UnwrapAwaitableResult(Type type)
    {
        Assert.True(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>));

        var result = type.GetGenericArguments()[0];
        Assert.True(result.IsGenericType && result.Name == "Result`2");
        return result.GetGenericArguments()[0];
    }

    private static IEnumerable<Type> UnwrapGenericArguments(Type type)
    {
        yield return type;

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in UnwrapGenericArguments(argument))
            {
                yield return nested;
            }
        }
    }

    private static void AssertResultType<T>(MethodInfo method)
    {
        Assert.Equal(typeof(T), UnwrapAwaitableResult(method.ReturnType));
    }
}
