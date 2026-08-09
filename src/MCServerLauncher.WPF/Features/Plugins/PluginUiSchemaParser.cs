using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MCServerLauncher.WPF.Features.Plugins;

public static partial class PluginUiSchemaParser
{
    public const string UiSchemaVersion = "mcsl.ui.v1";

    private const int MaxDepth = 16;
    private const int MaxNodes = 200;
    private const int MaxTextLength = 4096;

    private static readonly IReadOnlyDictionary<PluginUiComponentType, IReadOnlySet<string>> PropsByComponent =
        new Dictionary<PluginUiComponentType, IReadOnlySet<string>>
        {
            [PluginUiComponentType.View] = Set("Direction", "Gap", "Padding", "Align", "Justify"),
            [PluginUiComponentType.Text] = Set("Text", "Variant"),
            [PluginUiComponentType.Button] = Set("Text", "Variant", "Size", "Disabled", "OnClick"),
            [PluginUiComponentType.TextBox] = Set("Value", "Placeholder", "Disabled", "OnChanged"),
            [PluginUiComponentType.TextArea] = Set("Value", "Placeholder", "Disabled", "OnChanged"),
            [PluginUiComponentType.ToggleSwitch] = Set("IsOn", "Disabled", "OnChanged"),
            [PluginUiComponentType.CheckBox] = Set("IsChecked", "Label", "Disabled", "OnChanged"),
            [PluginUiComponentType.Select] = Set("Value", "Placeholder", "Options", "Disabled", "OnChanged"),
            [PluginUiComponentType.Card] = Set("Title", "Description"),
            [PluginUiComponentType.Tabs] = Set("Value", "Items", "OnChanged")
        };

    private static readonly IReadOnlySet<string> CommonProps = Set("Id", "Hidden");
    private static readonly IReadOnlySet<string> EventProps = Set("OnClick", "OnChanged");
    private static readonly IReadOnlySet<string> DesignTokenProps = Set("Gap", "Padding");
    private static readonly IReadOnlySet<string> DesignTokens = Set("None", "Xs", "Sm", "Md", "Lg", "Xl", "Spacing.Xs", "Spacing.Sm", "Spacing.Md", "Spacing.Lg", "Spacing.Xl");
    private static readonly IReadOnlySet<string> UnsafeCommandParamKeys = Set("__proto__", "prototype", "constructor");

    public static PluginUiParseResult Parse(string json)
    {
        JsonElement root;
        try
        {
            using var document = ParseDocument(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException firstException)
        {
            if (!TryNormalizeJson5ObjectKeys(json, out var normalized, out var diagnostic))
                return PluginUiParseResult.Failure([diagnostic]);

            try
            {
                using var document = ParseDocument(normalized);
                root = document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                return PluginUiParseResult.Failure([
                    new PluginUiDiagnostic("schema_parse_failed", "$", ex.Message.Length > 0 ? ex.Message : firstException.Message)
                ]);
            }
        }

        return Validate(root);
    }

    public static PluginUiParseResult ParseRuntime(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return Validate(document.RootElement.Clone());
        }
        catch (JsonException ex)
        {
            return PluginUiParseResult.Failure([
                new PluginUiDiagnostic("schema_parse_failed", "$", ex.Message.Length > 0 ? ex.Message : "Runtime UI schema is not valid JSON.")
            ]);
        }
    }

    private static JsonDocument ParseDocument(string json)
    {
        return JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
    }

    private static bool TryNormalizeJson5ObjectKeys(
        string source,
        out string normalized,
        out PluginUiDiagnostic diagnostic)
    {
        var builder = new StringBuilder(source.Length + 32);
        List<Json5Container> stack = [];
        normalized = string.Empty;
        diagnostic = new PluginUiDiagnostic("schema_parse_failed", "$", "UI schema parse failed.");

        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '"')
            {
                CopyDoubleQuotedString(source, builder, ref i);
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] is '/' or '*')
            {
                CopyComment(source, builder, ref i);
                continue;
            }

            var current = stack.Count > 0 ? stack[^1] : null;
            if (current is { IsObject: true, ExpectPropertyName: true })
            {
                if (char.IsWhiteSpace(c))
                {
                    builder.Append(c);
                    continue;
                }

                if (IsIdentifierStart(c))
                {
                    var start = i;
                    while (i + 1 < source.Length && IsIdentifierPart(source[i + 1]))
                        i++;
                    builder.Append('"');
                    builder.Append(source, start, i - start + 1);
                    builder.Append('"');
                    continue;
                }
            }

            switch (c)
            {
                case '{':
                    stack.Add(new Json5Container(isObject: true, expectPropertyName: true));
                    break;
                case '[':
                    stack.Add(new Json5Container(isObject: false, expectPropertyName: false));
                    break;
                case '}':
                case ']':
                    if (stack.Count == 0)
                    {
                        diagnostic = new PluginUiDiagnostic("schema_parse_failed", "$", "UI schema has an unmatched closing container.");
                        return false;
                    }
                    stack.RemoveAt(stack.Count - 1);
                    break;
                case ',':
                    if (current is { IsObject: true })
                        current.ExpectPropertyName = true;
                    break;
                case ':':
                    if (current is { IsObject: true })
                        current.ExpectPropertyName = false;
                    break;
            }

            builder.Append(c);
        }

        normalized = builder.ToString();
        return true;
    }

    public static PluginUiParseResult Validate(JsonElement root)
    {
        List<PluginUiDiagnostic> diagnostics = [];
        if (root.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(new PluginUiDiagnostic("schema_root_invalid", "$", "The UI schema root must be an object."));
            return PluginUiParseResult.Failure(diagnostics);
        }

        if (!TryGetString(root, "schema", out var schema) || schema != UiSchemaVersion)
            diagnostics.Add(new PluginUiDiagnostic("schema_version_invalid", "$.schema", $"The UI schema version must be '{UiSchemaVersion}'."));

        if (!root.TryGetProperty("root", out var rootNode))
        {
            diagnostics.Add(new PluginUiDiagnostic("schema_root_missing", "$.root", "The UI schema root node is required."));
            return PluginUiParseResult.Failure(diagnostics);
        }

        var counter = 0;
        var node = NormalizeNode(rootNode, "$.root", 0, diagnostics, ref counter);
        if (node is null || diagnostics.Count > 0)
            return PluginUiParseResult.Failure(diagnostics);

        return PluginUiParseResult.Success(new PluginUiSchema(UiSchemaVersion, node));
    }

    private static PluginUiNode? NormalizeNode(
        JsonElement value,
        string path,
        int depth,
        List<PluginUiDiagnostic> diagnostics,
        ref int counter)
    {
        if (depth > MaxDepth)
        {
            diagnostics.Add(new PluginUiDiagnostic("schema_depth_exceeded", path, $"UI schema depth exceeds {MaxDepth}."));
            return null;
        }

        counter++;
        if (counter > MaxNodes)
        {
            diagnostics.Add(new PluginUiDiagnostic("schema_node_limit_exceeded", path, $"UI schema node count exceeds {MaxNodes}."));
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(new PluginUiDiagnostic("node_invalid", path, "A UI node must be an object."));
            return null;
        }

        if (!TryGetString(value, "type", out var typeName) || !Enum.TryParse<PluginUiComponentType>(typeName, out var type))
        {
            diagnostics.Add(new PluginUiDiagnostic("component_unknown", $"{path}.type", $"Unknown UI component '{typeName ?? string.Empty}'."));
            return null;
        }

        var props = value.TryGetProperty("props", out var propsElement)
            ? NormalizeProps(type, propsElement, $"{path}.props", depth, diagnostics, ref counter)
            : [];
        var children = value.TryGetProperty("children", out var childrenElement)
            ? NormalizeChildren(childrenElement, $"{path}.children", depth, diagnostics, ref counter)
            : [];

        if (type == PluginUiComponentType.Tabs && !props.ContainsKey("Items"))
            diagnostics.Add(new PluginUiDiagnostic("tabs_items_missing", $"{path}.props.Items", "Tabs requires an Items array."));

        return PluginUiNode.Create(type, props, children);
    }

    private static Dictionary<string, object?> NormalizeProps(
        PluginUiComponentType type,
        JsonElement value,
        string path,
        int depth,
        List<PluginUiDiagnostic> diagnostics,
        ref int counter)
    {
        Dictionary<string, object?> props = [];
        if (value.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(new PluginUiDiagnostic("props_invalid", path, "Node props must be an object when present."));
            return props;
        }

        var allowed = PropsByComponent[type];
        foreach (var property in value.EnumerateObject())
        {
            var propPath = $"{path}.{property.Name}";
            if (!CommonProps.Contains(property.Name) && !allowed.Contains(property.Name))
            {
                diagnostics.Add(new PluginUiDiagnostic("prop_unknown", propPath, $"Property '{property.Name}' is not allowed on '{type}'."));
                continue;
            }

            if (property.Name is "ClassName" or "Style" or "Html")
            {
                diagnostics.Add(new PluginUiDiagnostic("prop_unsafe", propPath, $"Property '{property.Name}' is not allowed."));
                continue;
            }

            object? normalized = (type, property.Name) switch
            {
                _ when EventProps.Contains(property.Name) => NormalizeEventAction(property.Value, propPath, diagnostics),
                (PluginUiComponentType.Select, "Options") => NormalizeOptions(property.Value, propPath, diagnostics),
                (PluginUiComponentType.Tabs, "Items") => NormalizeTabItems(property.Value, propPath, depth, diagnostics, ref counter),
                _ => NormalizeValue(property.Value, propPath, diagnostics)
            };

            ValidatePropValue(property.Name, normalized, propPath, diagnostics);
            props[property.Name] = normalized;
        }

        return props;
    }

    private static object? NormalizeValue(JsonElement value, string path, List<PluginUiDiagnostic> diagnostics)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var text = value.GetString() ?? string.Empty;
                ValidateString(text, path, diagnostics);
                return text;
            case JsonValueKind.Number:
                return value.TryGetInt64(out var integer) ? integer : value.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.Array:
                return NormalizeArray(value, path, diagnostics);
            case JsonValueKind.Object:
                return NormalizeObject(value, path, diagnostics);
            default:
                diagnostics.Add(new PluginUiDiagnostic("prop_value_invalid", path, "Property value is not JSON-compatible."));
                return null;
        }
    }

    private static void ValidateString(string text, string path, List<PluginUiDiagnostic> diagnostics)
    {
        if (text.Length > MaxTextLength)
            diagnostics.Add(new PluginUiDiagnostic("prop_text_too_long", path, $"String property exceeds {MaxTextLength} characters."));
        if (text.Contains("<script", StringComparison.OrdinalIgnoreCase) || text.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(new PluginUiDiagnostic("prop_script_forbidden", path, "Script-like strings are not allowed."));
        if (text.StartsWith('{') && text.EndsWith('}') && !PluginUiBinding.IsAllowedBinding(text))
            diagnostics.Add(new PluginUiDiagnostic("binding_invalid", path, "Bindings must use {state.path} or an allowed formatter."));
    }

    private static IReadOnlyList<object?> NormalizeArray(JsonElement value, string path, List<PluginUiDiagnostic> diagnostics)
    {
        List<object?> items = [];
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            items.Add(NormalizeValue(item, $"{path}[{index}]", diagnostics));
            index++;
        }

        return new ReadOnlyCollection<object?>(items);
    }

    private static IReadOnlyDictionary<string, object?> NormalizeObject(JsonElement value, string path, List<PluginUiDiagnostic> diagnostics)
    {
        Dictionary<string, object?> properties = [];
        foreach (var property in value.EnumerateObject())
            properties[property.Name] = NormalizeValue(property.Value, $"{path}.{property.Name}", diagnostics);
        return new ReadOnlyDictionary<string, object?>(properties);
    }

    private static object? NormalizeEventAction(JsonElement value, string path, List<PluginUiDiagnostic> diagnostics)
    {
        if (value.ValueKind == JsonValueKind.String)
            return NormalizeValue(value, path, diagnostics);

        if (value.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(new PluginUiDiagnostic("event_action_invalid", path, "Event action must be a handler name or command binding object."));
            return null;
        }

        Dictionary<string, object?> action = [];
        foreach (var property in value.EnumerateObject())
        {
            var propertyPath = $"{path}.{property.Name}";
            switch (property.Name)
            {
                case "Command":
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        diagnostics.Add(new PluginUiDiagnostic("command_invalid", propertyPath, "Command must be a canonical extension command id."));
                    }
                    else
                    {
                        var command = property.Value.GetString() ?? string.Empty;
                        if (!CommandRegex().IsMatch(command))
                            diagnostics.Add(new PluginUiDiagnostic("command_invalid", propertyPath, "Command must be a canonical extension command id."));
                        action["Command"] = command;
                    }
                    break;
                case "Params":
                    if (property.Value.ValueKind != JsonValueKind.Object)
                    {
                        diagnostics.Add(new PluginUiDiagnostic("command_params_invalid", propertyPath, "Command Params must be an object."));
                    }
                    else
                    {
                        action["Params"] = NormalizeCommandParams(property.Value, propertyPath, diagnostics);
                    }
                    break;
                default:
                    diagnostics.Add(new PluginUiDiagnostic("command_property_unknown", propertyPath, $"Command binding property '{property.Name}' is not allowed."));
                    break;
            }
        }

        if (!action.ContainsKey("Command"))
            diagnostics.Add(new PluginUiDiagnostic("command_invalid", $"{path}.Command", "Command must be a canonical extension command id."));

        return new ReadOnlyDictionary<string, object?>(action);
    }

    private static IReadOnlyDictionary<string, object?> NormalizeCommandParams(JsonElement value, string path, List<PluginUiDiagnostic> diagnostics)
    {
        Dictionary<string, object?> parameters = [];
        foreach (var property in value.EnumerateObject())
        {
            var propertyPath = $"{path}.{property.Name}";
            if (UnsafeCommandParamKeys.Contains(property.Name))
            {
                diagnostics.Add(new PluginUiDiagnostic("command_param_key_unsafe", propertyPath, "Unsafe command parameter key is not allowed."));
                continue;
            }

            parameters[property.Name] = NormalizeCommandParamValue(property.Value, propertyPath, diagnostics);
        }

        return new ReadOnlyDictionary<string, object?>(parameters);
    }

    private static object? NormalizeCommandParamValue(JsonElement value, string path, List<PluginUiDiagnostic> diagnostics)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString() ?? string.Empty;
            ValidateCommandParamString(text, path, diagnostics);
            return text;
        }

        if (value.ValueKind == JsonValueKind.Object)
            return NormalizeCommandParams(value, path, diagnostics);

        if (value.ValueKind == JsonValueKind.Array)
        {
            List<object?> items = [];
            var index = 0;
            foreach (var item in value.EnumerateArray())
                items.Add(NormalizeCommandParamValue(item, $"{path}[{index++}]", diagnostics));
            return new ReadOnlyCollection<object?>(items);
        }

        return NormalizeValue(value, path, diagnostics);
    }

    private static void ValidateCommandParamString(string text, string path, List<PluginUiDiagnostic> diagnostics)
    {
        if (text.Length > MaxTextLength)
            diagnostics.Add(new PluginUiDiagnostic("prop_text_too_long", path, $"String property exceeds {MaxTextLength} characters."));
        if (text.Contains("<script", StringComparison.OrdinalIgnoreCase) || text.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(new PluginUiDiagnostic("prop_script_forbidden", path, "Script-like strings are not allowed."));
        if (text.StartsWith('{') && text.EndsWith('}') && text != "{event.value}" && !PluginUiBinding.IsAllowedBinding(text))
            diagnostics.Add(new PluginUiDiagnostic("binding_invalid", path, "Command parameter bindings must use {state.path}, {event.value}, or an allowed formatter."));
    }

    private static IReadOnlyList<PluginUiOption> NormalizeOptions(JsonElement value, string path, List<PluginUiDiagnostic> diagnostics)
    {
        List<PluginUiOption> options = [];
        if (value.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(new PluginUiDiagnostic("options_invalid", path, "Options must be an array."));
            return options;
        }

        HashSet<string> seen = [];
        var index = 0;
        foreach (var option in value.EnumerateArray())
        {
            var itemPath = $"{path}[{index}]";
            if (option.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new PluginUiDiagnostic("option_invalid", itemPath, "An option must be an object."));
                index++;
                continue;
            }

            if (!TryGetString(option, "Value", out var optionValue) || string.IsNullOrWhiteSpace(optionValue) || !seen.Add(optionValue))
                diagnostics.Add(new PluginUiDiagnostic("option_value_invalid", $"{itemPath}.Value", "Option Value is required and must be unique."));
            if (!TryGetString(option, "Text", out var optionText) || string.IsNullOrWhiteSpace(optionText))
                diagnostics.Add(new PluginUiDiagnostic("option_text_invalid", $"{itemPath}.Text", "Option Text is required."));

            if (!string.IsNullOrWhiteSpace(optionValue) && !string.IsNullOrWhiteSpace(optionText))
                options.Add(new PluginUiOption(optionValue, optionText));
            index++;
        }

        return new ReadOnlyCollection<PluginUiOption>(options);
    }

    private static IReadOnlyList<PluginUiTabItem> NormalizeTabItems(
        JsonElement value,
        string path,
        int depth,
        List<PluginUiDiagnostic> diagnostics,
        ref int counter)
    {
        List<PluginUiTabItem> items = [];
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            diagnostics.Add(new PluginUiDiagnostic("tabs_items_invalid", path, "Tabs Items must be a non-empty array."));
            return items;
        }

        HashSet<string> seen = [];
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var itemPath = $"{path}[{index}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new PluginUiDiagnostic("tabs_item_invalid", itemPath, "A tab item must be an object."));
                index++;
                continue;
            }

            if (!TryGetString(item, "Value", out var tabValue) || string.IsNullOrWhiteSpace(tabValue) || !seen.Add(tabValue))
                diagnostics.Add(new PluginUiDiagnostic("tabs_item_value_invalid", $"{itemPath}.Value", "Tab Value is required and must be unique."));
            if (!TryGetString(item, "Text", out var tabText) || string.IsNullOrWhiteSpace(tabText))
                diagnostics.Add(new PluginUiDiagnostic("tabs_item_text_invalid", $"{itemPath}.Text", "Tab Text is required."));

            var children = item.TryGetProperty("Children", out var childrenElement)
                ? NormalizeChildren(childrenElement, $"{itemPath}.Children", depth + 1, diagnostics, ref counter)
                : [];

            if (!string.IsNullOrWhiteSpace(tabValue) && !string.IsNullOrWhiteSpace(tabText))
                items.Add(new PluginUiTabItem(tabValue, tabText, children));
            index++;
        }

        return new ReadOnlyCollection<PluginUiTabItem>(items);
    }

    private static void ValidatePropValue(string key, object? value, string path, List<PluginUiDiagnostic> diagnostics)
    {
        if (EventProps.Contains(key))
        {
            if (value is string handler)
            {
                if (!HandlerRegex().IsMatch(handler))
                    diagnostics.Add(new PluginUiDiagnostic("handler_invalid", path, "Event handlers must be exported handler names."));
            }
            else if (value is IReadOnlyDictionary<string, object?> command)
            {
                if (!command.TryGetValue("Command", out var commandValue) || commandValue is not string commandText || !CommandRegex().IsMatch(commandText))
                    diagnostics.Add(new PluginUiDiagnostic("command_invalid", $"{path}.Command", "Command must be a canonical extension command id."));
            }
            else
            {
                diagnostics.Add(new PluginUiDiagnostic("event_action_invalid", path, "Event action must be a handler name or command binding object."));
            }
        }

        if (DesignTokenProps.Contains(key) && value is string token && !DesignTokens.Contains(token) && !PluginUiBinding.IsAllowedBinding(token))
            diagnostics.Add(new PluginUiDiagnostic("token_invalid", path, "Design token value is not in the allowlist."));
    }

    private static IReadOnlyList<PluginUiNode> NormalizeChildren(
        JsonElement value,
        string path,
        int depth,
        List<PluginUiDiagnostic> diagnostics,
        ref int counter)
    {
        List<PluginUiNode> children = [];
        if (value.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(new PluginUiDiagnostic("children_invalid", path, "Node children must be an array."));
            return children;
        }

        var index = 0;
        foreach (var child in value.EnumerateArray())
        {
            var node = NormalizeNode(child, $"{path}[{index}]", depth + 1, diagnostics, ref counter);
            if (node is not null)
                children.Add(node);
            index++;
        }

        return new ReadOnlyCollection<PluginUiNode>(children);
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        if (element.TryGetProperty(property, out var propertyElement) && propertyElement.ValueKind == JsonValueKind.String)
        {
            value = propertyElement.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static void CopyDoubleQuotedString(string source, StringBuilder builder, ref int index)
    {
        builder.Append(source[index]);
        var escaped = false;
        for (index++; index < source.Length; index++)
        {
            var c = source[index];
            builder.Append(c);
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '"')
                return;
        }
    }

    private static void CopyComment(string source, StringBuilder builder, ref int index)
    {
        builder.Append(source[index]);
        builder.Append(source[index + 1]);
        if (source[index + 1] == '/')
        {
            for (index += 2; index < source.Length; index++)
            {
                var c = source[index];
                builder.Append(c);
                if (c == '\n')
                    return;
            }

            return;
        }

        for (index += 2; index < source.Length; index++)
        {
            var c = source[index];
            builder.Append(c);
            if (c == '*' && index + 1 < source.Length && source[index + 1] == '/')
            {
                index++;
                builder.Append('/');
                return;
            }
        }
    }

    private static bool IsIdentifierStart(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_' or '$';

    private static bool IsIdentifierPart(char c) =>
        IsIdentifierStart(c) || c is >= '0' and <= '9';

    private sealed class Json5Container(bool isObject, bool expectPropertyName)
    {
        public bool IsObject { get; } = isObject;

        public bool ExpectPropertyName { get; set; } = expectPropertyName;
    }

    private static IReadOnlySet<string> Set(params string[] values) => new HashSet<string>(values, StringComparer.Ordinal);

    [GeneratedRegex(@"^[A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CommandRegex();

    [GeneratedRegex(@"^[A-Za-z_][\w]*$", RegexOptions.CultureInvariant)]
    private static partial Regex HandlerRegex();
}

public static class PluginUiSchemaCompiler
{
    private static readonly JsonSerializerOptions RuntimeJsonOptions = new()
    {
        WriteIndented = false
    };

    public static PluginUiCompileResult CompileToRuntimeJson(string source)
    {
        var parsed = PluginUiSchemaParser.Parse(source);
        if (!parsed.Ok || parsed.Schema is null)
            return PluginUiCompileResult.Failure(parsed.Diagnostics);

        return PluginUiCompileResult.Success(parsed.Schema, SerializeRuntimeJson(parsed.Schema));
    }

    public static string SerializeRuntimeJson(PluginUiSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var runtime = ToRuntimeObject(schema);
        return JsonSerializer.Serialize(runtime, RuntimeJsonOptions);
    }

    private static object ToRuntimeObject(PluginUiSchema schema) => new Dictionary<string, object?>
    {
        ["schema"] = schema.Schema,
        ["root"] = ToRuntimeObject(schema.Root)
    };

    private static object ToRuntimeObject(PluginUiNode node) => new Dictionary<string, object?>
    {
        ["type"] = node.Type.ToString(),
        ["props"] = NormalizeValue(node.Props),
        ["children"] = node.Children.Select(ToRuntimeObject).ToArray()
    };

    private static object? NormalizeValue(object? value)
    {
        if (value is null)
            return null;
        if (value is string or bool or int or long or float or double or decimal)
            return value;
        if (value is IReadOnlyDictionary<string, object?> dictionary)
            return dictionary.ToDictionary(pair => pair.Key, pair => NormalizeValue(pair.Value), StringComparer.Ordinal);
        if (value is IReadOnlyList<object?> list)
            return list.Select(NormalizeValue).ToArray();
        if (value is JsonElement element)
            return NormalizeJsonElement(element);
        return value.ToString();
    }

    private static object? NormalizeJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(property => property.Name, property => NormalizeJsonElement(property.Value), StringComparer.Ordinal),
        JsonValueKind.Array => element.EnumerateArray().Select(NormalizeJsonElement).ToArray(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when element.TryGetDouble(out var number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => null
    };
}
