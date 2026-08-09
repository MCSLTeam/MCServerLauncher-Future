using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MCServerLauncher.WPF.Features.Plugins;

public sealed record PluginUiSchema(string Schema, PluginUiNode Root);

public sealed record PluginUiNode(
    PluginUiComponentType Type,
    IReadOnlyDictionary<string, object?> Props,
    IReadOnlyList<PluginUiNode> Children)
{
    public static PluginUiNode Create(
        PluginUiComponentType type,
        IReadOnlyDictionary<string, object?>? props = null,
        IReadOnlyList<PluginUiNode>? children = null)
    {
        return new PluginUiNode(
            type,
            new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(props ?? new Dictionary<string, object?>())),
            new ReadOnlyCollection<PluginUiNode>((children ?? []).ToArray()));
    }
}

public enum PluginUiComponentType
{
    View,
    Text,
    Button,
    TextBox,
    TextArea,
    ToggleSwitch,
    CheckBox,
    Select,
    Card,
    Tabs
}

public sealed record PluginUiOption(string Value, string Text);

public sealed record PluginUiTabItem(string Value, string Text, IReadOnlyList<PluginUiNode> Children);

public sealed record PluginUiDiagnostic(string Code, string Path, string Message);

public sealed record PluginUiParseResult(
    bool Ok,
    PluginUiSchema? Schema,
    IReadOnlyList<PluginUiDiagnostic> Diagnostics)
{
    public static PluginUiParseResult Success(PluginUiSchema schema) =>
        new(true, schema, []);

    public static PluginUiParseResult Failure(IReadOnlyList<PluginUiDiagnostic> diagnostics) =>
        new(false, null, diagnostics);
}

public sealed record PluginUiCompileResult(
    bool Ok,
    PluginUiSchema? Schema,
    string? RuntimeJson,
    IReadOnlyList<PluginUiDiagnostic> Diagnostics)
{
    public static PluginUiCompileResult Success(PluginUiSchema schema, string runtimeJson) =>
        new(true, schema, runtimeJson, []);

    public static PluginUiCompileResult Failure(IReadOnlyList<PluginUiDiagnostic> diagnostics) =>
        new(false, null, null, diagnostics);
}
