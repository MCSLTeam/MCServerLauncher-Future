using System.Globalization;
using System.Reflection;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Daemon.API.Application;

namespace MCServerLauncher.Daemon.ApiTests;

public sealed class PublicApiBaselineTests
{
    [Fact]
    public void PublicApiMatchesTheReviewedFirstReleaseBaseline()
    {
        var expected = ReadBaseline("DaemonApi.PublicApi.txt");
        var actual = PublicApiText.Render(typeof(IDaemonApplication).Assembly);
        WriteDiagnosticBaseline("MCSL_DAEMON_API_BASELINE_OUTPUT", actual);

        AssertBaselineMatches("Daemon API", "DaemonApi.PublicApi.txt", expected, actual);
    }

    [Fact]
    public void CommonContractsPublicApiMatchesTheReviewedFirstReleaseBaseline()
    {
        var expected = ReadBaseline("CommonContracts.PublicApi.txt");
        var actual = PublicApiText.RenderCanonicalCommonContracts(typeof(InstanceReport).Assembly);
        WriteDiagnosticBaseline("MCSL_COMMON_CONTRACTS_BASELINE_OUTPUT", actual);

        AssertBaselineMatches("Common Contracts", "CommonContracts.PublicApi.txt", expected, actual);
    }

    [Fact]
    public void InstanceReportProcessIdAdditionOrRemovalChangesTheCommonContractsBaseline()
    {
        var assembly = typeof(InstanceReport).Assembly;
        var completeSurface = PublicApiText.Render(assembly, "MCServerLauncher.Common.Contracts");
        var surfaceWithoutProcessId = PublicApiText.Render(
            assembly,
            "MCServerLauncher.Common.Contracts",
            member => member is not PropertyInfo
            {
                DeclaringType: not null,
                Name: nameof(InstanceReport.ProcessId)
            } property || property.DeclaringType != typeof(InstanceReport));
        const string processIdProperty =
            "property MCServerLauncher.Common.Contracts.Instances.InstanceReport.ProcessId[] : System.Nullable<System.Int32> | get=public | set=public";

        // These two surfaces model both directions: removing the existing member or adding it back.
        Assert.Contains(processIdProperty, completeSurface.Split('\n'));
        Assert.DoesNotContain(processIdProperty, surfaceWithoutProcessId.Split('\n'));
        Assert.NotEqual(completeSurface, surfaceWithoutProcessId);
    }

    private static string ReadBaseline(string fileName)
    {
        var baselinePath = Path.Combine(AppContext.BaseDirectory, "Baselines", fileName);
        return string.Join(
            '\n',
            File.ReadLines(baselinePath)
                .Where(line => !line.StartsWith('#'))
                .Select(line => line.TrimEnd())
                .SkipWhile(string.IsNullOrEmpty)
                .Reverse()
                .SkipWhile(string.IsNullOrEmpty)
                .Reverse());
    }

    private static void WriteDiagnosticBaseline(string environmentVariable, string value)
    {
        var diagnosticOutputPath = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(diagnosticOutputPath))
        {
            File.WriteAllText(diagnosticOutputPath, value);
        }
    }

    private static void AssertBaselineMatches(
        string surfaceName,
        string baselineFileName,
        string expected,
        string actual)
    {
        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            $"The {surfaceName} public surface differs from its reviewed baseline.{Environment.NewLine}" +
            $"Review the ABI change and update Baselines/{baselineFileName} deliberately.{Environment.NewLine}" +
            $"Actual surface:{Environment.NewLine}{actual}");
    }
}

internal static class PublicApiText
{
    private const BindingFlags MemberFlags = BindingFlags.Public |
                                               BindingFlags.NonPublic |
                                               BindingFlags.Instance |
                                               BindingFlags.Static |
                                               BindingFlags.DeclaredOnly;

    public static string Render(Assembly assembly) =>
        Render(assembly, static _ => true, static _ => true);

    public static string Render(Assembly assembly, string namespacePrefix) =>
        Render(
            assembly,
            type => type.Namespace is not null &&
                    (type.Namespace.Equals(namespacePrefix, StringComparison.Ordinal) ||
                     type.Namespace.StartsWith($"{namespacePrefix}.", StringComparison.Ordinal)),
            static _ => true);

    public static string RenderCanonicalCommonContracts(Assembly assembly) =>
        Render(
            assembly,
            type => type.Namespace is not null &&
                    (type.Namespace.Equals("MCServerLauncher.Common.Contracts", StringComparison.Ordinal) ||
                     type.Namespace.StartsWith("MCServerLauncher.Common.Contracts.", StringComparison.Ordinal)),
            static _ => true);

    public static string Render(
        Assembly assembly,
        string namespacePrefix,
        Func<MemberInfo, bool> memberFilter) =>
        Render(
            assembly,
            type => type.Namespace is not null &&
                    (type.Namespace.Equals(namespacePrefix, StringComparison.Ordinal) ||
                     type.Namespace.StartsWith($"{namespacePrefix}.", StringComparison.Ordinal)),
            memberFilter);

    private static string Render(
        Assembly assembly,
        Func<Type, bool> typeFilter,
        Func<MemberInfo, bool> memberFilter)
    {
        var lines = assembly
            .GetExportedTypes()
            .Where(typeFilter)
            .OrderBy(type => FormatType(type), StringComparer.Ordinal)
            .SelectMany(type => RenderType(type, memberFilter))
            .ToArray();

        return string.Join('\n', lines);
    }

    private static IEnumerable<string> RenderType(Type type, Func<MemberInfo, bool> memberFilter)
    {
        var baseType = type.BaseType is null ? "-" : FormatType(type.BaseType);
        var interfaces = string.Join(",", type.GetInterfaces().Select(FormatType).Order(StringComparer.Ordinal));
        var constraints = FormatGenericConstraints(type.GetGenericArguments().Where(argument => argument.IsGenericParameter));

        yield return $"type {FormatType(type)} | kind={GetTypeKind(type)} | visibility={GetTypeVisibility(type)} | abstract={type.IsAbstract} | sealed={type.IsSealed} | base={baseType} | interfaces={NullIfEmpty(interfaces)} | constraints={NullIfEmpty(constraints)}";

        foreach (var constructor in type.GetConstructors(MemberFlags).Where(IsApiVisible).Where(member => memberFilter(member)).OrderBy(FormatConstructor, StringComparer.Ordinal))
        {
            yield return FormatConstructor(constructor);
        }

        foreach (var field in type.GetFields(MemberFlags).Where(IsApiVisible).Where(member => memberFilter(member)).OrderBy(FormatField, StringComparer.Ordinal))
        {
            yield return FormatField(field);
        }

        foreach (var property in type.GetProperties(MemberFlags).Where(IsApiVisible).Where(member => memberFilter(member)).OrderBy(FormatProperty, StringComparer.Ordinal))
        {
            yield return FormatProperty(property);
        }

        foreach (var eventInfo in type.GetEvents(MemberFlags).Where(IsApiVisible).Where(member => memberFilter(member)).OrderBy(FormatEvent, StringComparer.Ordinal))
        {
            yield return FormatEvent(eventInfo);
        }

        foreach (var method in type
                     .GetMethods(MemberFlags)
                     .Where(IsApiVisible)
                     .Where(method => !IsPropertyOrEventAccessor(method))
                     .Where(member => memberFilter(member))
                     .OrderBy(FormatMethod, StringComparer.Ordinal))
        {
            yield return FormatMethod(method);
        }
    }

    private static string FormatConstructor(ConstructorInfo constructor) =>
        $"ctor {FormatType(constructor.DeclaringType!)}({FormatParameters(constructor.GetParameters())}) | visibility={GetVisibility(constructor)}";

    private static string FormatField(FieldInfo field)
    {
        var constant = field.IsLiteral
            ? FormatConstant(field.GetRawConstantValue())
            : "-";
        return $"field {FormatType(field.DeclaringType!)}.{field.Name} : {FormatType(field.FieldType)} | visibility={GetVisibility(field)} | static={field.IsStatic} | readonly={field.IsInitOnly} | literal={field.IsLiteral} | value={constant}";
    }

    private static string FormatProperty(PropertyInfo property)
    {
        var getter = property.GetMethod is null ? "-" : GetVisibility(property.GetMethod);
        var setter = property.SetMethod is null || !IsApiVisible(property.SetMethod)
            ? "-"
            : GetVisibility(property.SetMethod);
        return $"property {FormatType(property.DeclaringType!)}.{property.Name}[{FormatParameters(property.GetIndexParameters())}] : {FormatType(property.PropertyType)} | get={getter} | set={setter}";
    }

    private static string FormatEvent(EventInfo eventInfo)
    {
        var add = eventInfo.AddMethod is null ? "-" : GetVisibility(eventInfo.AddMethod);
        var remove = eventInfo.RemoveMethod is null ? "-" : GetVisibility(eventInfo.RemoveMethod);
        return $"event {FormatType(eventInfo.DeclaringType!)}.{eventInfo.Name} : {FormatType(eventInfo.EventHandlerType!)} | add={add} | remove={remove}";
    }

    private static string FormatMethod(MethodInfo method)
    {
        var genericArguments = method.GetGenericArguments();
        var genericSuffix = genericArguments.Length == 0
            ? string.Empty
            : $"<{string.Join(",", genericArguments.Select(argument => argument.Name))}>";
        var constraints = FormatGenericConstraints(genericArguments.Where(argument => argument.IsGenericParameter));

        return $"method {FormatType(method.DeclaringType!)}.{method.Name}{genericSuffix}({FormatParameters(method.GetParameters())}) -> {FormatType(method.ReturnType)} | visibility={GetVisibility(method)} | static={method.IsStatic} | abstract={method.IsAbstract} | virtual={method.IsVirtual} | constraints={NullIfEmpty(constraints)}";
    }

    private static string FormatParameters(IEnumerable<ParameterInfo> parameters) =>
        string.Join(",", parameters.Select(FormatParameter));

    private static string FormatParameter(ParameterInfo parameter)
    {
        var parameterType = parameter.ParameterType;
        var modifier = parameter.IsOut
            ? "out "
            : parameterType.IsByRef && parameter.IsIn
                ? "in "
                : parameterType.IsByRef
                    ? "ref "
                    : string.Empty;
        if (parameterType.IsByRef)
        {
            parameterType = parameterType.GetElementType()!;
        }

        var defaultValue = parameter.HasDefaultValue
            ? FormatConstant(parameter.DefaultValue)
            : "-";
        return $"{modifier}{FormatType(parameterType)} {parameter.Name} optional={parameter.IsOptional} default={defaultValue}";
    }

    private static string FormatGenericConstraints(IEnumerable<Type> genericParameters)
    {
        return string.Join(
            ";",
            genericParameters.Select(parameter =>
            {
                var attributes = parameter.GenericParameterAttributes;
                var parts = new List<string>();
                var variance = attributes & GenericParameterAttributes.VarianceMask;
                if (variance != GenericParameterAttributes.None)
                {
                    parts.Add(variance.ToString());
                }

                var specialConstraints = attributes & GenericParameterAttributes.SpecialConstraintMask;
                if (specialConstraints != GenericParameterAttributes.None)
                {
                    parts.Add(specialConstraints.ToString());
                }

                parts.AddRange(parameter.GetGenericParameterConstraints().Select(FormatType).Order(StringComparer.Ordinal));
                return $"{parameter.Name}:[{string.Join(",", parts)}]";
            }));
    }

    private static string FormatType(Type type)
    {
        if (type.IsByRef)
        {
            return $"{FormatType(type.GetElementType()!)}&";
        }

        if (type.IsPointer)
        {
            return $"{FormatType(type.GetElementType()!)}*";
        }

        if (type.IsArray)
        {
            return $"{FormatType(type.GetElementType()!)}[{new string(',', type.GetArrayRank() - 1)}]";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (!type.IsGenericType)
        {
            return (type.FullName ?? type.Name).Replace('+', '.');
        }

        var definitionName = string.Join(
            ".",
            (type.GetGenericTypeDefinition().FullName ?? type.Name)
                .Replace('+', '.')
                .Split('.')
                .Select(segment =>
                {
                    var tickIndex = segment.IndexOf('`');
                    return tickIndex < 0 ? segment : segment[..tickIndex];
                }));

        return $"{definitionName}<{string.Join(",", type.GetGenericArguments().Select(FormatType))}>";
    }

    private static string FormatConstant(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        char character => $"'{character}'",
        bool boolean => boolean ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? value.ToString()!,
        _ => value.ToString() ?? "null"
    };

    private static string GetTypeKind(Type type) =>
        type.IsInterface ? "interface" :
        type.IsEnum ? "enum" :
        typeof(Delegate).IsAssignableFrom(type) ? "delegate" :
        type.IsValueType ? "struct" :
        "class";

    private static string GetTypeVisibility(Type type) =>
        type.IsNested ? "nested-public" : "public";

    private static string GetVisibility(MethodBase method) =>
        method.IsPublic ? "public" :
        method.IsFamilyOrAssembly ? "protected-internal" :
        method.IsFamily ? "protected" :
        "non-api";

    private static string GetVisibility(FieldInfo field) =>
        field.IsPublic ? "public" :
        field.IsFamilyOrAssembly ? "protected-internal" :
        field.IsFamily ? "protected" :
        "non-api";

    private static bool IsApiVisible(MethodBase method) =>
        method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static bool IsApiVisible(FieldInfo field) =>
        field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;

    private static bool IsApiVisible(PropertyInfo property) =>
        property.GetAccessors(nonPublic: true).Any(IsApiVisible);

    private static bool IsApiVisible(EventInfo eventInfo) =>
        (eventInfo.AddMethod is not null && IsApiVisible(eventInfo.AddMethod)) ||
        (eventInfo.RemoveMethod is not null && IsApiVisible(eventInfo.RemoveMethod));

    private static bool IsPropertyOrEventAccessor(MethodInfo method) =>
        method.IsSpecialName &&
        (method.Name.StartsWith("get_", StringComparison.Ordinal) ||
         method.Name.StartsWith("set_", StringComparison.Ordinal) ||
         method.Name.StartsWith("add_", StringComparison.Ordinal) ||
         method.Name.StartsWith("remove_", StringComparison.Ordinal));

    private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? "-" : value;

}
