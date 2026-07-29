using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace MCServerLauncher.ProtocolTests;

/// <summary>
/// The plugin fixtures are not members of <c>MCServerLauncher.slnx</c>. A solution build therefore
/// cannot resolve them against the solution configuration and unsets theirs entirely, at which
/// point they fall back to the SDK default of Debug — so a <c>-c Release</c> run used to load
/// Debug-compiled plugins while reporting itself as a Release run.
/// </summary>
public sealed class FixtureBuildConfigurationTests
{
    [Theory]
    [InlineData("InstanceHealth.dll")]
    [InlineData("SdkGeneratedHealth.dll")]
    [InlineData("HandwrittenAdapterProbe.dll")]
    [InlineData("Throwing.dll")]
    public void FixturePluginsAreBuiltInTheSameConfigurationAsTheRunLoadingThem(string fixtureAssembly)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fixtureAssembly);
        Assert.True(File.Exists(path), $"Fixture {fixtureAssembly} was not copied next to the tests.");

        // Compare against this assembly rather than a hardcoded expectation, so the test is
        // meaningful under -c Release without failing a routine local Debug run.
        var expected = OptimizationsDisabled(typeof(FixtureBuildConfigurationTests).Assembly.Location);
        var actual = OptimizationsDisabled(path);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Reads <see cref="DebuggableAttribute" /> straight out of the PE metadata. Loading the
    /// fixture would put it in the default context, which the plugin isolation tests rely on not
    /// happening. The JIT-optimizer-disabled flag is the only configuration marker that survives
    /// compilation.
    /// </summary>
    private static bool OptimizationsDisabled(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        foreach (var handle in reader.CustomAttributes)
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (!IsDebuggableAttribute(reader, attribute))
                continue;

            // Roslyn emits DebuggableAttribute(DebuggingModes). Debug sets DisableOptimizations;
            // Release emits IgnoreSymbolStoreSequencePoints alone.
            var value = attribute.DecodeValue(new DebuggingModesProvider());
            if (value.FixedArguments.Length == 1 && value.FixedArguments[0].Value is int modes)
                return (modes & (int)DebuggableAttribute.DebuggingModes.DisableOptimizations) != 0;
        }

        return false;
    }

    private static bool IsDebuggableAttribute(MetadataReader reader, CustomAttribute attribute)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference)
            return false;

        var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
        if (constructor.Parent.Kind != HandleKind.TypeReference)
            return false;

        var type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
        return reader.GetString(type.Name) == nameof(DebuggableAttribute) &&
               reader.GetString(type.Namespace) == typeof(DebuggableAttribute).Namespace;
    }

    /// <summary>
    /// The attribute's only argument is an enum, which the decoder resolves to its underlying
    /// int32; nothing else in the blob is needed here.
    /// </summary>
    private sealed class DebuggingModesProvider : ICustomAttributeTypeProvider<object>
    {
        public object GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode;
        public object GetSystemType() => typeof(Type);
        public object GetSZArrayType(object elementType) => elementType;
        public object GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => typeof(object);
        public object GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => typeof(object);
        public object GetTypeFromSerializedName(string name) => typeof(object);
        public PrimitiveTypeCode GetUnderlyingEnumType(object type) => PrimitiveTypeCode.Int32;
        public bool IsSystemType(object type) => false;
    }
}
