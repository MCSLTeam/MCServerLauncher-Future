using System.Reflection;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management;
using LegacyGetInstanceSettingsResult = MCServerLauncher.Common.ProtoType.Action.GetInstanceSettingsResult;
using LegacyInstanceInstallMetadata = MCServerLauncher.Common.ProtoType.Action.InstanceInstallMetadata;
using LegacyUpdateInstanceSettingsParameter = MCServerLauncher.Common.ProtoType.Action.UpdateInstanceSettingsParameter;
using LegacyUpdateInstanceSettingsResult = MCServerLauncher.Common.ProtoType.Action.UpdateInstanceSettingsResult;

namespace MCServerLauncher.ProtocolTests;

public sealed class CoreLegacyDtoBoundaryTests
{
    private const BindingFlags DeclaredMemberFlags =
        BindingFlags.Public |
        BindingFlags.NonPublic |
        BindingFlags.Instance |
        BindingFlags.Static |
        BindingFlags.DeclaredOnly;

    private static readonly HashSet<Type> ForbiddenTypes =
    [
        typeof(InstanceFactorySetting),
        typeof(LegacyGetInstanceSettingsResult),
        typeof(LegacyUpdateInstanceSettingsParameter),
        typeof(LegacyUpdateInstanceSettingsResult),
        typeof(LegacyInstanceInstallMetadata)
    ];

    [Fact]
    [Trait("Category", "CompiledBoundary")]
    public void ManagementAndApplicationCore_DoNotReferenceLegacyInstanceDtos()
    {
        var assembly = typeof(InstanceManager).Assembly;
        var coreTypes = assembly
            .GetTypes()
            .Where(type => IsCoreNamespace(type.Namespace))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        var violations = new List<string>();

        foreach (var type in coreTypes)
        {
            foreach (var field in type.GetFields(DeclaredMemberFlags))
                RecordViolation(violations, type, $"field {field.Name}", field.FieldType);

            foreach (var property in type.GetProperties(DeclaredMemberFlags))
            {
                RecordViolation(violations, type, $"property {property.Name}", property.PropertyType);
                foreach (var parameter in property.GetIndexParameters())
                    RecordViolation(violations, type, $"property {property.Name} parameter {parameter.Name}", parameter.ParameterType);
            }

            foreach (var constructor in type.GetConstructors(DeclaredMemberFlags))
            {
                foreach (var parameter in constructor.GetParameters())
                    RecordViolation(violations, type, $"constructor parameter {parameter.Name}", parameter.ParameterType);
            }

            foreach (var method in type.GetMethods(DeclaredMemberFlags))
            {
                RecordViolation(violations, type, $"method {method.Name} return", method.ReturnType);
                foreach (var parameter in method.GetParameters())
                    RecordViolation(violations, type, $"method {method.Name} parameter {parameter.Name}", parameter.ParameterType);
            }
        }

        Assert.True(
            violations.Count == 0,
            "Legacy instance DTO references escaped the temporary Remote.Action allowlist:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static bool IsCoreNamespace(string? namespaceName)
    {
        return namespaceName is not null &&
               (namespaceName.Equals("MCServerLauncher.Daemon.Management", StringComparison.Ordinal) ||
                namespaceName.StartsWith("MCServerLauncher.Daemon.Management.", StringComparison.Ordinal) ||
                namespaceName.Equals("MCServerLauncher.Daemon.ApplicationCore", StringComparison.Ordinal) ||
                namespaceName.StartsWith("MCServerLauncher.Daemon.ApplicationCore.", StringComparison.Ordinal));
    }

    private static void RecordViolation(
        ICollection<string> violations,
        Type declaringType,
        string member,
        Type signatureType)
    {
        if (!TryFindForbiddenType(signatureType, out var forbiddenType))
            return;

        violations.Add(
            $"{declaringType.FullName}: {member} references {forbiddenType.FullName} through {signatureType}");
    }

    private static bool TryFindForbiddenType(Type type, out Type forbiddenType)
    {
        if (ForbiddenTypes.Contains(type))
        {
            forbiddenType = type;
            return true;
        }

        if (type.HasElementType && type.GetElementType() is { } elementType &&
            TryFindForbiddenType(elementType, out forbiddenType))
        {
            return true;
        }

        if (type.IsGenericType)
        {
            foreach (var genericArgument in type.GetGenericArguments())
            {
                if (TryFindForbiddenType(genericArgument, out forbiddenType))
                    return true;
            }
        }

        forbiddenType = null!;
        return false;
    }
}
