using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace MCServerLauncher.WPF.Features.Plugins;

public sealed record PluginThemeRuntime(
    string Schema,
    IReadOnlyDictionary<string, string> Colors,
    IReadOnlyDictionary<string, double> Spacing);

public sealed record PluginThemeParseResult(
    bool Ok,
    PluginThemeRuntime? Theme,
    string? RuntimeJson,
    IReadOnlyList<PluginUiDiagnostic> Diagnostics)
{
    public static PluginThemeParseResult Success(PluginThemeRuntime theme, string? runtimeJson = null) =>
        new(true, theme, runtimeJson, []);

    public static PluginThemeParseResult Failure(IReadOnlyList<PluginUiDiagnostic> diagnostics) =>
        new(false, null, null, diagnostics);
}

public static class PluginThemeRuntimeParser
{
    public const string ThemeSchemaVersion = "mcsl.theme.v1";

    private const int MaxThemeJsonBytes = 64 * 1024;

    private static readonly IReadOnlySet<string> AllowedColors = Set(
        "accent",
        "background",
        "surface",
        "textPrimary",
        "textMuted",
        "success",
        "warning",
        "danger");

    private static readonly IReadOnlySet<string> AllowedSpacing = Set("xs", "sm", "md", "lg", "xl");

    public static PluginThemeParseResult Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            var validated = Validate(document.RootElement.Clone());
            return validated.Ok && validated.Theme is not null
                ? PluginThemeParseResult.Success(validated.Theme, SerializeRuntimeJson(validated.Theme))
                : validated;
        }
        catch (JsonException ex)
        {
            return PluginThemeParseResult.Failure([
                new PluginUiDiagnostic("theme_parse_failed", "$", ex.Message.Length > 0 ? ex.Message : "Theme is not valid JSON.")
            ]);
        }
    }

    public static PluginThemeParseResult ParseRuntime(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return Validate(document.RootElement.Clone());
        }
        catch (JsonException ex)
        {
            return PluginThemeParseResult.Failure([
                new PluginUiDiagnostic("theme_parse_failed", "$", ex.Message.Length > 0 ? ex.Message : "Runtime theme is not valid JSON.")
            ]);
        }
    }

    public static string SerializeRuntimeJson(PluginThemeRuntime theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return JsonSerializer.Serialize(new
        {
            schema = ThemeSchemaVersion,
            colors = theme.Colors,
            spacing = theme.Spacing
        });
    }

    public static ResourceDictionary ToResourceDictionary(PluginThemeRuntime theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var dictionary = new ResourceDictionary();
        foreach (var (key, value) in theme.Colors)
            dictionary[$"McslExtensionColor{ToPascalCase(key)}"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        foreach (var (key, value) in theme.Spacing)
            dictionary[$"McslExtensionSpacing{ToPascalCase(key)}"] = value;
        return dictionary;
    }

    private static PluginThemeParseResult Validate(JsonElement root)
    {
        List<PluginUiDiagnostic> diagnostics = [];
        if (root.GetRawText().Length > MaxThemeJsonBytes)
            diagnostics.Add(new PluginUiDiagnostic("theme_too_large", "$", $"Theme JSON exceeds {MaxThemeJsonBytes} characters."));
        if (root.ValueKind != JsonValueKind.Object)
            return PluginThemeParseResult.Failure([new PluginUiDiagnostic("theme_root_invalid", "$", "Theme root must be an object.")]);

        if (!TryGetString(root, "schema", out var schema) || schema != ThemeSchemaVersion)
            diagnostics.Add(new PluginUiDiagnostic("theme_schema_invalid", "$.schema", $"Theme schema must be '{ThemeSchemaVersion}'."));

        var colors = NormalizeColors(root.TryGetProperty("colors", out var colorsElement) ? colorsElement : default, diagnostics);
        var spacing = NormalizeSpacing(root.TryGetProperty("spacing", out var spacingElement) ? spacingElement : default, diagnostics);
        if (diagnostics.Count > 0)
            return PluginThemeParseResult.Failure(diagnostics);

        return PluginThemeParseResult.Success(new PluginThemeRuntime(
            ThemeSchemaVersion,
            new ReadOnlyDictionary<string, string>(colors),
            new ReadOnlyDictionary<string, double>(spacing)));
    }

    private static Dictionary<string, string> NormalizeColors(JsonElement element, List<PluginUiDiagnostic> diagnostics)
    {
        Dictionary<string, string> colors = new(StringComparer.Ordinal);
        if (element.ValueKind == JsonValueKind.Undefined)
            return colors;
        if (element.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(new PluginUiDiagnostic("theme_colors_invalid", "$.colors", "colors must be an object."));
            return colors;
        }

        foreach (var property in element.EnumerateObject())
        {
            var path = $"$.colors.{property.Name}";
            if (!AllowedColors.Contains(property.Name))
            {
                diagnostics.Add(new PluginUiDiagnostic("theme_color_unknown", path, "Theme color token is not supported."));
                continue;
            }
            if (property.Value.ValueKind != JsonValueKind.String || !TryGetString(property.Value, out var color) || color is null || !IsHexColor(color))
            {
                diagnostics.Add(new PluginUiDiagnostic("theme_color_invalid", path, "Theme colors must be #RRGGBB hex values."));
                continue;
            }

            colors[property.Name] = color.ToLowerInvariant();
        }

        return colors;
    }

    private static Dictionary<string, double> NormalizeSpacing(JsonElement element, List<PluginUiDiagnostic> diagnostics)
    {
        Dictionary<string, double> spacing = new(StringComparer.Ordinal);
        if (element.ValueKind == JsonValueKind.Undefined)
            return spacing;
        if (element.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(new PluginUiDiagnostic("theme_spacing_invalid", "$.spacing", "spacing must be an object."));
            return spacing;
        }

        foreach (var property in element.EnumerateObject())
        {
            var path = $"$.spacing.{property.Name}";
            if (!AllowedSpacing.Contains(property.Name))
            {
                diagnostics.Add(new PluginUiDiagnostic("theme_spacing_unknown", path, "Theme spacing token is not supported."));
                continue;
            }
            if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDouble(out var value) || value < 0 || value > 96 || Math.Abs(value % 1) > double.Epsilon)
            {
                diagnostics.Add(new PluginUiDiagnostic("theme_spacing_invalid", path, "Theme spacing values must be integers between 0 and 96."));
                continue;
            }

            spacing[property.Name] = value;
        }

        return spacing;
    }

    private static bool TryGetString(JsonElement element, string property, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(property, out var child))
            return false;
        return TryGetString(child, out value);
    }

    private static bool TryGetString(JsonElement element, out string? value)
    {
        value = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        return value is not null;
    }

    private static bool IsHexColor(string value) =>
        value.Length == 7 &&
        value[0] == '#' &&
        int.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);

    private static string ToPascalCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}
