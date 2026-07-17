using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.State;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApiTests;

public sealed class PublicSurfaceTests
{
    private static readonly string[] ForbiddenNamespacePrefixes =
    [
        "TouchSocket",
        "MessagePipe",
        "Serilog",
        "MCServerLauncher.Daemon.Management",
        "MCServerLauncher.Daemon.Remote",
        "MCServerLauncher.Daemon.Utils",
        "Microsoft.Extensions.DependencyInjection"
    ];

    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "TouchSocket",
        "MessagePipe",
        "Serilog",
        "MCServerLauncher.Daemon"
    ];

    private static readonly HashSet<string> ApprovedAssemblyReferences =
    [
        "MCServerLauncher.Common",
        "RustyOptions",
        "Microsoft.Extensions.Logging.Abstractions",
        "System.Collections",
        "System.Collections.Immutable",
        "System.Runtime",
        "System.Text.Json",
        "System.Threading"
    ];

    [Fact]
    public void ApiAssemblyReferencesOnlyApprovedContractDependencies()
    {
        var references = typeof(IDaemonApplication).Assembly.GetReferencedAssemblies();

        Assert.All(references, reference =>
        {
            var name = reference.Name ?? throw new InvalidOperationException("Assembly reference name was missing.");
            Assert.Contains(name, ApprovedAssemblyReferences);
        });
    }

    [Fact]
    public void CompletePublicAndProtectedSurfaceDoesNotLeakForbiddenTypes()
    {
        var publicTypes = typeof(IDaemonApplication).Assembly.GetExportedTypes();

        Assert.DoesNotContain(publicTypes, IsForbidden);

        foreach (var exposedType in publicTypes.SelectMany(GetExposedTypes))
        {
            Assert.False(IsForbidden(exposedType), $"Forbidden public API type: {exposedType}");
            Assert.False(IsDisposableHandle(exposedType), $"Disposable public API type: {exposedType}");
            Assert.False(IsMutableCollection(exposedType), $"Mutable collection public API type: {exposedType}");
        }
    }

    [Fact]
    public void SurfaceTraversalCoversEveryAbiMemberKind()
    {
        var exposedTypes = GetExposedTypes(typeof(SurfaceProbe<>)).ToHashSet();

        Assert.Contains(typeof(Exception), exposedTypes);
        Assert.Contains(typeof(IComparable), exposedTypes);
        Assert.Contains(typeof(Stream), exposedTypes);
        Assert.Contains(typeof(Uri), exposedTypes);
        Assert.Contains(typeof(Version), exposedTypes);
        Assert.Contains(typeof(DateTime), exposedTypes);
        Assert.Contains(typeof(EventHandler), exposedTypes);
        Assert.Contains(typeof(TimeSpan), exposedTypes);
        Assert.Contains(typeof(Guid), exposedTypes);
        Assert.Contains(typeof(TextReader), exposedTypes);
    }

    [Theory]
    [InlineData(typeof(IInstanceSnapshotSource))]
    [InlineData(typeof(InstanceCatalogSnapshot))]
    public void SnapshotLookupUsesTheStandardConditionalNullabilityContract(Type declaringType)
    {
        var method = declaringType.GetMethod(nameof(IInstanceSnapshotSource.TryGet))
                     ?? throw new InvalidOperationException("The snapshot lookup method was missing.");
        var snapshot = method.GetParameters().Single(parameter => parameter.Name == "snapshot");

        Assert.True(snapshot.IsOut);
        Assert.Equal(NullabilityState.Nullable, new NullabilityInfoContext().Create(snapshot).WriteState);
        Assert.True(snapshot.GetCustomAttribute<NotNullWhenAttribute>() is { ReturnValue: true });
    }

    [Fact]
    public void PublicResultSurfacesAlwaysUseDaemonError()
    {
        var resultTypes = typeof(IDaemonApplication).Assembly
            .GetExportedTypes()
            .SelectMany(GetExposedTypes)
            .SelectMany(FlattenType)
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<,>))
            .ToArray();

        Assert.NotEmpty(resultTypes);
        Assert.All(resultTypes, resultType =>
            Assert.Equal(typeof(DaemonError), resultType.GetGenericArguments()[1]));

        var resultInterfaces = typeof(IDaemonApplication).Assembly
            .GetExportedTypes()
            .SelectMany(GetExposedTypes)
            .SelectMany(FlattenType)
            .Where(type => type.IsInterface && type.Name.StartsWith("IResult", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(resultInterfaces);
    }

    private static IEnumerable<Type> GetExposedTypes(Type type)
    {
        yield return type;

        if (type.BaseType is not null)
        {
            yield return type.BaseType;
        }

        foreach (var interfaceType in type.GetInterfaces())
        {
            yield return interfaceType;
        }

        foreach (var genericParameter in type.GetGenericArguments().Where(argument => argument.IsGenericParameter))
        {
            foreach (var constraint in genericParameter.GetGenericParameterConstraints())
            {
                yield return constraint;
            }
        }

        const BindingFlags memberFlags = BindingFlags.Public |
                                         BindingFlags.NonPublic |
                                         BindingFlags.Instance |
                                         BindingFlags.Static |
                                         BindingFlags.DeclaredOnly;

        foreach (var constructor in type.GetConstructors(memberFlags).Where(IsApiVisible))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var field in type.GetFields(memberFlags).Where(IsApiVisible))
        {
            yield return field.FieldType;
        }

        foreach (var property in type.GetProperties(memberFlags).Where(IsApiVisible))
        {
            yield return property.PropertyType;

            foreach (var parameter in property.GetIndexParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var eventInfo in type.GetEvents(memberFlags).Where(IsApiVisible))
        {
            if (eventInfo.EventHandlerType is not null)
            {
                yield return eventInfo.EventHandlerType;
            }
        }

        foreach (var method in type.GetMethods(memberFlags).Where(IsApiVisible))
        {
            yield return method.ReturnType;

            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }

            foreach (var genericParameter in method.GetGenericArguments().Where(argument => argument.IsGenericParameter))
            {
                foreach (var constraint in genericParameter.GetGenericParameterConstraints())
                {
                    yield return constraint;
                }
            }
        }
    }

    private static IEnumerable<Type> FlattenType(Type type)
    {
        yield return type;

        if (type.HasElementType)
        {
            foreach (var nestedType in FlattenType(type.GetElementType()!))
            {
                yield return nestedType;
            }
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var nestedType in FlattenType(argument))
                {
                    yield return nestedType;
                }
            }
        }
    }

    private static bool IsApiVisible(MethodBase method) =>
        method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static bool IsApiVisible(FieldInfo field) =>
        field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;

    private static bool IsApiVisible(PropertyInfo property) =>
        property.GetAccessors(nonPublic: true).Any(IsApiVisible);

    private static bool IsApiVisible(EventInfo eventInfo) =>
        (eventInfo.AddMethod is not null && IsApiVisible(eventInfo.AddMethod)) ||
        (eventInfo.RemoveMethod is not null && IsApiVisible(eventInfo.RemoveMethod));

    private static bool IsForbidden(Type type)
    {
        if (type.HasElementType)
        {
            return IsForbidden(type.GetElementType()!);
        }

        if (type == typeof(IServiceProvider))
        {
            return true;
        }

        var namespaceName = type.Namespace;
        if (namespaceName is not null && ForbiddenNamespacePrefixes.Any(namespaceName.StartsWith))
        {
            return true;
        }

        var assemblyName = type.Assembly.GetName().Name;
        if (assemblyName is not null &&
            ForbiddenAssemblyPrefixes.Any(assemblyName.StartsWith) &&
            assemblyName != "MCServerLauncher.Daemon.API")
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(IsForbidden);
    }

    private static bool IsDisposableHandle(Type type)
    {
        if (type.HasElementType)
        {
            return IsDisposableHandle(type.GetElementType()!);
        }

        if (type == typeof(Task))
        {
            return false;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return type.GetGenericArguments().Any(IsDisposableHandle);
        }

        return typeof(IDisposable).IsAssignableFrom(type) ||
               typeof(IAsyncDisposable).IsAssignableFrom(type) ||
               (type.IsGenericType && type.GetGenericArguments().Any(IsDisposableHandle));
    }

    private static bool IsMutableCollection(Type type)
    {
        if (type.IsArray)
        {
            return true;
        }

        if (type.HasElementType)
        {
            return IsMutableCollection(type.GetElementType()!);
        }

        if (type.Namespace == "System.Collections.Immutable")
        {
            return false;
        }

        if (type == typeof(Memory<>) || type == typeof(ArraySegment<>))
        {
            return true;
        }

        if (typeof(IList).IsAssignableFrom(type) || typeof(IDictionary).IsAssignableFrom(type))
        {
            return true;
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (IsMutableCollectionDefinition(definition))
            {
                return true;
            }

            if (type.GetInterfaces().Any(interfaceType =>
                    interfaceType.IsGenericType &&
                    IsMutableCollectionDefinition(interfaceType.GetGenericTypeDefinition())) ||
                type.GetGenericArguments().Any(IsMutableCollection))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMutableCollectionDefinition(Type definition) =>
        definition == typeof(ICollection<>) ||
        definition == typeof(IList<>) ||
        definition == typeof(IDictionary<,>) ||
        definition == typeof(ISet<>) ||
        definition == typeof(List<>) ||
        definition == typeof(Dictionary<,>) ||
        definition == typeof(HashSet<>) ||
        definition == typeof(Memory<>) ||
        definition == typeof(ArraySegment<>);

    private class SurfaceProbe<T> : Exception, IComparable
        where T : Stream
    {
        public SurfaceProbe(Uri constructorValue)
        {
        }

        public Version? Field = null;

        public DateTime Property { get; init; }

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public TimeSpan Method<TResult>(Guid parameter)
            where TResult : TextReader => default;

        public int CompareTo(object? obj) => 0;
    }
}
