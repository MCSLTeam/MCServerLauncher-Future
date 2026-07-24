using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using NuGet.Versioning;

namespace MCServerLauncher.Daemon.Plugins;

internal static class GeneratedPluginMetadataReader
{
    private const string MetadataAttributeNamespace = "MCServerLauncher.Daemon.API.Plugins";
    private const string MetadataAttributeName = "GeneratedDaemonPluginMetadataAttribute";
    private const string DaemonApiAssemblyName = "MCServerLauncher.Daemon.API";
    private const string StringType = "System.String";
    private static readonly StringAttributeTypeProvider TypeProvider = new();

    internal static void Validate(PluginManifest manifest) =>
        _ = ReadValidatedImage(manifest);

    internal static ValidatedPluginAssemblyImage ReadValidatedImage(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        byte[] image;
        ImmutableArray<GeneratedPluginMetadata> metadata;
        try
        {
            using var stream = new FileStream(
                manifest.EntryAssemblyPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var snapshot = new MemoryStream();
            stream.CopyTo(snapshot);
            image = snapshot.ToArray();
            using var imageStream = new MemoryStream(image, writable: false);
            using var peReader = new PEReader(imageStream, PEStreamOptions.PrefetchMetadata);
            if (!peReader.HasMetadata)
            {
                throw new PluginManifestException(
                    "generated_metadata_missing",
                    $"Entry assembly '{manifest.EntryAssembly}' does not contain CLR metadata.");
            }

            metadata = Read(peReader.GetMetadataReader());
        }
        catch (PluginManifestException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is BadImageFormatException or IOException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException)
        {
            throw new PluginManifestException(
                "generated_metadata_invalid",
                $"Generated assembly metadata for plugin '{manifest.Identity.Id}' could not be read.",
                exception);
        }

        if (metadata.IsEmpty)
        {
            throw new PluginManifestException(
                "generated_metadata_missing",
                $"Entry assembly '{manifest.EntryAssembly}' does not contain generated plugin metadata.");
        }

        var matchingEntries = metadata
            .Where(candidate => string.Equals(candidate.EntryType, manifest.EntryType, StringComparison.Ordinal))
            .ToArray();
        if (matchingEntries.Length > 1)
        {
            throw new PluginManifestException(
                "generated_metadata_duplicate",
                $"Entry type '{manifest.EntryType}' has duplicate generated plugin metadata.");
        }

        if (matchingEntries.Length == 0)
        {
            var generatedEntries = string.Join(", ", metadata.Select(static candidate => candidate.EntryType));
            throw new PluginManifestException(
                "generated_metadata_mismatch",
                $"Generated assembly metadata mismatch for 'entry.type': manifest '{manifest.EntryType}', " +
                $"generated entries '{generatedEntries}'.");
        }

        var generated = matchingEntries[0];
        RequireMatch(manifest, "package.id", manifest.Identity.Id, generated.PackageId);
        RequireMatch(manifest, "package.version", manifest.Identity.Version, generated.PackageVersion);
        RequireMatch(manifest, "entry.assembly", manifest.EntryAssembly, generated.EntryAssembly);
        RequireMatch(
            manifest,
            "requires.api",
            manifest.ApiVersionRange.ToNormalizedString(),
            generated.ApiRange);

        var manifestFeatures = manifest.Features
            .Select(static feature => feature.Value)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        if (!manifestFeatures.SequenceEqual(generated.Features, StringComparer.Ordinal))
        {
            throw new PluginManifestException(
                "generated_metadata_mismatch",
                $"Generated assembly metadata mismatch for 'requires.features' in plugin " +
                $"'{manifest.Identity.Id}'.");
        }

        RequireMatch(manifest, "manifest digest", manifest.ManifestDigest, generated.ManifestDigest);
        return new ValidatedPluginAssemblyImage(image);
    }

    internal sealed class ValidatedPluginAssemblyImage
    {
        private readonly byte[] _image;

        internal ValidatedPluginAssemblyImage(byte[] image)
        {
            _image = image;
        }

        internal Stream OpenReadStream() => new MemoryStream(_image, writable: false);
    }

    private static ImmutableArray<GeneratedPluginMetadata> Read(MetadataReader reader)
    {
        var builder = ImmutableArray.CreateBuilder<GeneratedPluginMetadata>();
        foreach (var attributeHandle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (!IsGeneratedMetadataAttribute(reader, attribute.Constructor))
                continue;

            CustomAttributeValue<string> decoded;
            try
            {
                decoded = attribute.DecodeValue(TypeProvider);
            }
            catch (Exception exception) when (
                exception is BadImageFormatException or InvalidOperationException)
            {
                throw new PluginManifestException(
                    "generated_metadata_invalid",
                    "Generated plugin metadata has an invalid custom-attribute value.",
                    exception);
            }

            if (decoded.FixedArguments.Length != 7 || !decoded.NamedArguments.IsEmpty)
            {
                throw new PluginManifestException(
                    "generated_metadata_invalid",
                    "Generated plugin metadata must contain exactly seven string arguments and no named arguments.");
            }

            var values = new string[decoded.FixedArguments.Length];
            for (var index = 0; index < decoded.FixedArguments.Length; index++)
            {
                var argument = decoded.FixedArguments[index];
                if (!string.Equals(argument.Type, StringType, StringComparison.Ordinal) ||
                    argument.Value is not string value)
                {
                    throw new PluginManifestException(
                        "generated_metadata_invalid",
                        "Every generated plugin metadata argument must be a non-null string.");
                }

                values[index] = value;
            }

            builder.Add(Parse(values));
        }

        return builder.ToImmutable();
    }

    private static GeneratedPluginMetadata Parse(IReadOnlyList<string> values)
    {
        var packageId = RequireCanonical(values[0], "package.id");
        var packageVersion = RequireCanonical(values[1], "package.version");
        var entryAssembly = RequireCanonical(values[2], "entry.assembly");
        var entryType = RequireCanonical(values[3], "entry.type");
        var apiRange = RequireCanonical(values[4], "requires.api");
        var features = ParseFeatures(values[5]);
        var digest = RequireCanonical(values[6], "manifest digest");

        try
        {
            _ = ProtocolIdentifier.ValidatePluginId(packageId, nameof(packageId));
            var version = NuGetVersion.Parse(packageVersion);
            if (!string.Equals(version.ToNormalizedString(), packageVersion, StringComparison.Ordinal))
                throw new FormatException("Package version is not normalized.");

            var range = VersionRange.Parse(apiRange);
            if (!string.Equals(range.ToNormalizedString(), apiRange, StringComparison.Ordinal))
                throw new FormatException("API version range is not normalized.");
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new PluginManifestException(
                "generated_metadata_invalid",
                "Generated plugin identity or API range is not canonical.",
                exception);
        }

        if (!entryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(entryAssembly) ||
            !string.Equals(Path.GetFileName(entryAssembly), entryAssembly, StringComparison.Ordinal))
        {
            throw new PluginManifestException(
                "generated_metadata_invalid",
                "Generated plugin metadata field 'entry.assembly' is not a canonical DLL file name.");
        }

        if (entryType.IndexOfAny([',', '/', '\\']) >= 0 || entryType.Any(char.IsControl))
        {
            throw new PluginManifestException(
                "generated_metadata_invalid",
                "Generated plugin metadata field 'entry.type' is not a canonical CLR type name.");
        }

        if (digest.Length != 64 || digest.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new PluginManifestException(
                "generated_metadata_invalid",
                "Generated plugin manifest digest must be a lowercase SHA-256 value.");
        }

        return new GeneratedPluginMetadata(
            packageId,
            packageVersion,
            entryAssembly,
            entryType,
            apiRange,
            features,
            digest);
    }

    private static ImmutableArray<string> ParseFeatures(string encoded)
    {
        if (encoded.Length == 0)
            return ImmutableArray<string>.Empty;
        if (encoded.Contains('\r'))
        {
            throw new PluginManifestException(
                "generated_metadata_invalid",
                "Generated plugin features must use line-feed separators.");
        }

        var values = encoded.Split('\n');
        var builder = ImmutableArray.CreateBuilder<string>(values.Length);
        string? previous = null;
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new PluginManifestException(
                    "generated_metadata_invalid",
                    "Generated plugin features contain an empty or non-canonical item.");
            }

            try
            {
                _ = new PluginFeature(value);
            }
            catch (ArgumentException exception)
            {
                throw new PluginManifestException(
                    "generated_metadata_invalid",
                    $"Generated plugin feature '{value}' is not canonical.",
                    exception);
            }

            if (!FeatureCatalog.IsKnown(value))
            {
                throw new PluginManifestException(
                    "generated_metadata_invalid",
                    $"Generated plugin feature '{value}' is unknown.");
            }

            if (previous is not null && StringComparer.Ordinal.Compare(previous, value) >= 0)
            {
                throw new PluginManifestException(
                    "generated_metadata_invalid",
                    "Generated plugin features must be unique and sorted with ordinal comparison.");
            }

            builder.Add(value);
            previous = value;
        }

        return builder.ToImmutable();
    }

    private static string RequireCanonical(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.Any(char.IsControl))
        {
            throw new PluginManifestException(
                "generated_metadata_invalid",
                $"Generated plugin metadata field '{field}' must be a non-empty canonical string.");
        }

        return value;
    }

    private static void RequireMatch(
        PluginManifest manifest,
        string field,
        string manifestValue,
        string generatedValue)
    {
        if (!string.Equals(manifestValue, generatedValue, StringComparison.Ordinal))
        {
            throw new PluginManifestException(
                "generated_metadata_mismatch",
                $"Generated assembly metadata mismatch for '{field}' in plugin '{manifest.Identity.Id}': " +
                $"manifest '{manifestValue}', generated '{generatedValue}'.");
        }
    }

    private static bool IsGeneratedMetadataAttribute(MetadataReader reader, EntityHandle constructorHandle)
    {
        if (constructorHandle.Kind != HandleKind.MemberReference)
            return false;

        var constructor = reader.GetMemberReference((MemberReferenceHandle)constructorHandle);
        if (!string.Equals(reader.GetString(constructor.Name), ".ctor", StringComparison.Ordinal) ||
            constructor.Parent.Kind != HandleKind.TypeReference)
        {
            return false;
        }

        var attributeType = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
        if (!string.Equals(reader.GetString(attributeType.Namespace), MetadataAttributeNamespace, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(attributeType.Name), MetadataAttributeName, StringComparison.Ordinal) ||
            attributeType.ResolutionScope.Kind != HandleKind.AssemblyReference)
        {
            return false;
        }

        var assemblyReference = reader.GetAssemblyReference((AssemblyReferenceHandle)attributeType.ResolutionScope);
        return string.Equals(reader.GetString(assemblyReference.Name), DaemonApiAssemblyName, StringComparison.Ordinal);
    }

    private sealed class StringAttributeTypeProvider : ICustomAttributeTypeProvider<string>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.String => StringType,
            _ => $"primitive:{typeCode}",
        };

        public string GetSystemType() => "System.Type";

        public bool IsSystemType(string type) => string.Equals(type, "System.Type", StringComparison.Ordinal);

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            return JoinTypeName(reader.GetString(type.Namespace), reader.GetString(type.Name));
        }

        public string GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            return JoinTypeName(reader.GetString(type.Namespace), reader.GetString(type.Name));
        }

        public string GetTypeFromSerializedName(string name) => name;

        public PrimitiveTypeCode GetUnderlyingEnumType(string type) =>
            throw new BadImageFormatException($"Unexpected enum type '{type}' in generated plugin metadata.");

        private static string JoinTypeName(string @namespace, string name) =>
            string.IsNullOrEmpty(@namespace) ? name : @namespace + "." + name;
    }

    private sealed record GeneratedPluginMetadata(
        string PackageId,
        string PackageVersion,
        string EntryAssembly,
        string EntryType,
        string ApiRange,
        ImmutableArray<string> Features,
        string ManifestDigest);
}
