using System.Reflection;
using System.Runtime.CompilerServices;
using MCServerLauncher.Daemon.API.Events;

namespace MCServerLauncher.Daemon.ApiTests.Events;

public sealed class DaemonEventContractTests
{
    [Fact]
    public void EventFieldKindValuesAreFrozen()
    {
        Assert.Equal(0, (int)DaemonEventFieldKind.Missing);
        Assert.Equal(1, (int)DaemonEventFieldKind.ExplicitNull);
        Assert.Equal(2, (int)DaemonEventFieldKind.Value);
    }

    [Fact]
    public void DefaultEventFieldIsMissingAndGuardsValue()
    {
        DaemonEventField<string> field = default;

        Assert.Equal(DaemonEventFieldKind.Missing, field.Kind);
        Assert.Equal(DaemonEventField<string>.Missing, field);
        Assert.Throws<InvalidOperationException>(() => field.Value);
    }

    [Fact]
    public void EventFieldDistinguishesExplicitNullAndValue()
    {
        var explicitNull = DaemonEventField<string>.ExplicitNull;
        var value = DaemonEventField<string>.FromValue("value");

        Assert.Equal(DaemonEventFieldKind.ExplicitNull, explicitNull.Kind);
        Assert.Throws<InvalidOperationException>(() => explicitNull.Value);
        Assert.Equal(DaemonEventFieldKind.Value, value.Kind);
        Assert.Equal("value", value.Value);
        Assert.Throws<ArgumentNullException>(() => DaemonEventField<string>.FromValue(null!));
        Assert.Throws<ArgumentNullException>(() => DaemonEventField<int?>.FromValue(null));
    }

    [Fact]
    public void EventFieldUsesClrValueEqualityAndHashing()
    {
        var first = DaemonEventField<string>.FromValue(new string(['v', 'a', 'l', 'u', 'e']));
        var second = DaemonEventField<string>.FromValue("value");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, DaemonEventField<string>.ExplicitNull);
    }

    [Fact]
    public void EventFieldToStringHandlesEveryStateWithoutReadingAnAbsentValue()
    {
        Assert.Equal(
            "DaemonEventField { Kind = Missing }",
            DaemonEventField<string>.Missing.ToString());
        Assert.Equal(
            "DaemonEventField { Kind = ExplicitNull }",
            DaemonEventField<string>.ExplicitNull.ToString());
        Assert.Equal(
            "DaemonEventField { Kind = Value, Value = value }",
            DaemonEventField<string>.FromValue("value").ToString());
    }

    [Fact]
    public void DefaultFilterIsWildcardAndGuardsValue()
    {
        DaemonEventFilter<string> filter = default;

        Assert.Equal(DaemonEventFieldKind.Missing, filter.Kind);
        Assert.Equal(DaemonEventFilter<string>.Wildcard, filter);
        Assert.Throws<InvalidOperationException>(() => filter.Value);
    }

    [Fact]
    public void FilterDistinguishesExplicitNullAndExactValue()
    {
        var explicitNull = DaemonEventFilter<string>.ExplicitNull;
        var exact = DaemonEventFilter<string>.Exact("value");

        Assert.Equal(DaemonEventFieldKind.ExplicitNull, explicitNull.Kind);
        Assert.Throws<InvalidOperationException>(() => explicitNull.Value);
        Assert.Equal(DaemonEventFieldKind.Value, exact.Kind);
        Assert.Equal("value", exact.Value);
        Assert.Throws<ArgumentNullException>(() => DaemonEventFilter<string>.Exact(null!));
        Assert.Throws<ArgumentNullException>(() => DaemonEventFilter<int?>.Exact(null));
    }

    [Fact]
    public void FilterUsesClrValueEqualityAndHashing()
    {
        var first = DaemonEventFilter<string>.Exact(new string(['v', 'a', 'l', 'u', 'e']));
        var second = DaemonEventFilter<string>.Exact("value");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, DaemonEventFilter<string>.ExplicitNull);
    }

    [Fact]
    public void FilterToStringHandlesEveryStateWithoutReadingAnAbsentValue()
    {
        Assert.Equal(
            "DaemonEventFilter { Kind = Missing }",
            DaemonEventFilter<string>.Wildcard.ToString());
        Assert.Equal(
            "DaemonEventFilter { Kind = ExplicitNull }",
            DaemonEventFilter<string>.ExplicitNull.ToString());
        Assert.Equal(
            "DaemonEventFilter { Kind = Value, Value = value }",
            DaemonEventFilter<string>.Exact("value").ToString());
    }

    [Fact]
    public void EventRecordUsesValueEqualityForEnvelopeAndFields()
    {
        var first = new DaemonEvent<string, string>(
            41,
            1783677000000,
            DaemonEventField<string>.ExplicitNull,
            DaemonEventField<string>.FromValue("data"));
        var second = new DaemonEvent<string, string>(
            41,
            1783677000000,
            DaemonEventField<string>.ExplicitNull,
            DaemonEventField<string>.FromValue("data"));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { Sequence = 42 });
    }

    [Fact]
    public void EventRecordToStringHandlesMissingAndExplicitNullFields()
    {
        var value = new DaemonEvent<string, string>(
            41,
            1783677000000,
            DaemonEventField<string>.ExplicitNull,
            DaemonEventField<string>.Missing);

        var text = value.ToString();

        Assert.Contains("Sequence = 41", text, StringComparison.Ordinal);
        Assert.Contains("Timestamp = 1783677000000", text, StringComparison.Ordinal);
        Assert.Contains("Meta = DaemonEventField { Kind = ExplicitNull }", text, StringComparison.Ordinal);
        Assert.Contains("Data = DaemonEventField { Kind = Missing }", text, StringComparison.Ordinal);
    }

    [Fact]
    public void StructReadonlyAndEventInitOnlyMetadataAreFrozen()
    {
        Assert.True(typeof(DaemonEventField<>).IsDefined(typeof(IsReadOnlyAttribute), inherit: false));
        Assert.True(typeof(DaemonEventFilter<>).IsDefined(typeof(IsReadOnlyAttribute), inherit: false));

        foreach (var propertyName in new[] { "Sequence", "Timestamp", "Meta", "Data" })
        {
            var property = typeof(DaemonEvent<,>).GetProperty(propertyName)
                ?? throw new InvalidOperationException($"Missing positional event property '{propertyName}'.");
            var setter = property.SetMethod
                ?? throw new InvalidOperationException($"Missing positional event setter '{propertyName}'.");

            Assert.Contains(
                setter.ReturnParameter.GetRequiredCustomModifiers(),
                modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");
        }
    }

    [Fact]
    public void PublicContractHasNoPublicFactoryBypassOrTransportTypes()
    {
        Assert.Empty(typeof(DaemonEventField<>).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(typeof(DaemonEventFilter<>).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        Type[] contractTypes =
        [
            typeof(DaemonEventFieldKind),
            typeof(DaemonEventField<>),
            typeof(DaemonEventFilter<>),
            typeof(DaemonEvent<,>)
        ];

        var signatureTypeNames = contractTypes
            .SelectMany(GetPublicSignatureTypes)
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.DoesNotContain(signatureTypeNames, name => name.Contains("JsonElement", StringComparison.Ordinal));
        Assert.DoesNotContain(signatureTypeNames, name => name.Contains("JsonTypeInfo", StringComparison.Ordinal));
        Assert.DoesNotContain(signatureTypeNames, name => name.Contains("Utf8", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(signatureTypeNames, name => name.StartsWith("TouchSocket", StringComparison.Ordinal));
    }

    private static IEnumerable<Type> GetPublicSignatureTypes(Type type)
    {
        yield return type;

        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var parameter in constructor.GetParameters())
                yield return parameter.ParameterType;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            yield return property.PropertyType;

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
                yield return parameter.ParameterType;
        }
    }
}
