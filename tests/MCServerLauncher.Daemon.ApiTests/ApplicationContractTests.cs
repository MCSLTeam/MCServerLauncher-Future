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
    public void DaemonApplicationComposesTheSixDomainServices()
    {
        var properties = typeof(IDaemonApplication).GetProperties();

        Assert.Collection(
            properties.OrderBy(property => property.Name),
            property => Assert.Equal(typeof(IEventRuleApplication), property.PropertyType),
            property => Assert.Equal(typeof(IFileApplication), property.PropertyType),
            property => Assert.Equal(typeof(IInstanceApplication), property.PropertyType),
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
            typeof(IProvisioningApplication)
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
        var exposedTypes = typeof(IFileApplication).GetMethods()
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                .Append(UnwrapAwaitableResult(method.ReturnType)))
            .SelectMany(UnwrapGenericArguments)
            .ToArray();

        Assert.DoesNotContain(exposedTypes, type => typeof(IDisposable).IsAssignableFrom(type));
        Assert.DoesNotContain(exposedTypes, type => typeof(Stream).IsAssignableFrom(type));
    }

    [Fact]
    public void FileApplicationPreservesIndependentFileAndDirectoryOperations()
    {
        var methods = typeof(IFileApplication).GetMethods().ToDictionary(method => method.Name);

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
    }

    [Fact]
    public void InstanceApplicationReturnsParityCompleteCreateAndSettingsResults()
    {
        var methods = typeof(IInstanceApplication).GetMethods().ToDictionary(method => method.Name);

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
