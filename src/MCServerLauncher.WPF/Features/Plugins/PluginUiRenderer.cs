using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MCServerLauncher.WPF.Features.Plugins;

public sealed class PluginUiRenderer
{
    public Task<FrameworkElement> RenderAsync(
        PluginUiSchema schema,
        IReadOnlyDictionary<string, object?> state,
        IPluginUiHost host,
        Dispatcher dispatcher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(dispatcher);

        var operation = dispatcher.InvokeAsync(
            () => RenderOnCurrentThread(schema, state, host),
            DispatcherPriority.Normal,
            cancellationToken);
        return operation.Task;
    }

    public FrameworkElement RenderOnCurrentThread(
        PluginUiSchema schema,
        IReadOnlyDictionary<string, object?> state,
        IPluginUiHost host)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return RenderNode(schema.Root, state, host);
    }

    private static FrameworkElement RenderNode(
        PluginUiNode node,
        IReadOnlyDictionary<string, object?> state,
        IPluginUiHost host)
    {
        if (PluginUiBinding.ResolveBoolean(GetProp(node, "Hidden"), state))
            return CollapsedPlaceholder();

        return node.Type switch
        {
            PluginUiComponentType.View => RenderView(node, state, host),
            PluginUiComponentType.Text => RenderText(node, state),
            PluginUiComponentType.Button => RenderButton(node, state, host),
            PluginUiComponentType.TextBox => RenderTextBox(node, state, host, acceptsReturn: false),
            PluginUiComponentType.TextArea => RenderTextBox(node, state, host, acceptsReturn: true),
            PluginUiComponentType.ToggleSwitch => RenderToggle(node, state, host),
            PluginUiComponentType.CheckBox => RenderCheckBox(node, state, host),
            PluginUiComponentType.Select => RenderSelect(node, state, host),
            PluginUiComponentType.Card => RenderCard(node, state, host),
            PluginUiComponentType.Tabs => RenderTabs(node, state, host),
            _ => CollapsedPlaceholder()
        };
    }

    private static FrameworkElement RenderView(PluginUiNode node, IReadOnlyDictionary<string, object?> state, IPluginUiHost host)
    {
        var panel = new StackPanel
        {
            Orientation = PluginUiBinding.ResolveText(GetProp(node, "Direction"), state) == "Horizontal"
                ? Orientation.Horizontal
                : Orientation.Vertical,
            Margin = ToThickness(GetProp(node, "Padding"), state)
        };

        foreach (var child in node.Children)
        {
            var element = RenderNode(child, state, host);
            element.Margin = AddGap(element.Margin, GetProp(node, "Gap"), state);
            panel.Children.Add(element);
        }

        return panel;
    }

    private static FrameworkElement RenderText(PluginUiNode node, IReadOnlyDictionary<string, object?> state)
    {
        var variant = PluginUiBinding.ResolveText(GetProp(node, "Variant"), state);
        return new TextBlock
        {
            Text = PluginUiBinding.ResolveText(GetProp(node, "Text"), state),
            TextWrapping = TextWrapping.Wrap,
            FontSize = variant == "Title" ? 18 : variant == "Label" ? 12 : 14,
            FontWeight = variant == "Title" ? FontWeights.SemiBold : FontWeights.Normal,
            Opacity = variant is "Subtitle" or "Label" ? 0.75 : 1.0
        };
    }

    private static FrameworkElement RenderButton(PluginUiNode node, IReadOnlyDictionary<string, object?> state, IPluginUiHost host)
    {
        var button = new Button
        {
            Content = NonEmpty(PluginUiBinding.ResolveText(GetProp(node, "Text"), state), "Button"),
            IsEnabled = !PluginUiBinding.ResolveBoolean(GetProp(node, "Disabled"), state),
            Padding = ToButtonPadding(GetProp(node, "Size"), state)
        };
        button.Click += (_, _) => Emit(host, node, "OnClick", "click", state);
        return button;
    }

    private static FrameworkElement RenderTextBox(
        PluginUiNode node,
        IReadOnlyDictionary<string, object?> state,
        IPluginUiHost host,
        bool acceptsReturn)
    {
        var textBox = new TextBox
        {
            Text = PluginUiBinding.ResolveText(GetProp(node, "Value"), state),
            IsEnabled = !PluginUiBinding.ResolveBoolean(GetProp(node, "Disabled"), state),
            AcceptsReturn = acceptsReturn,
            MinHeight = acceptsReturn ? 80 : 0
        };
        textBox.TextChanged += (_, _) => Emit(host, node, "OnChanged", "changed", state, textBox.Text);
        return textBox;
    }

    private static FrameworkElement RenderToggle(PluginUiNode node, IReadOnlyDictionary<string, object?> state, IPluginUiHost host)
    {
        var checkBox = new CheckBox
        {
            IsChecked = PluginUiBinding.ResolveBoolean(GetProp(node, "IsOn"), state),
            IsEnabled = !PluginUiBinding.ResolveBoolean(GetProp(node, "Disabled"), state)
        };
        checkBox.Checked += (_, _) => Emit(host, node, "OnChanged", "changed", state, true);
        checkBox.Unchecked += (_, _) => Emit(host, node, "OnChanged", "changed", state, false);
        return checkBox;
    }

    private static FrameworkElement RenderCheckBox(PluginUiNode node, IReadOnlyDictionary<string, object?> state, IPluginUiHost host)
    {
        var checkBox = new CheckBox
        {
            Content = PluginUiBinding.ResolveText(GetProp(node, "Label"), state),
            IsChecked = PluginUiBinding.ResolveBoolean(GetProp(node, "IsChecked"), state),
            IsEnabled = !PluginUiBinding.ResolveBoolean(GetProp(node, "Disabled"), state)
        };
        checkBox.Checked += (_, _) => Emit(host, node, "OnChanged", "changed", state, true);
        checkBox.Unchecked += (_, _) => Emit(host, node, "OnChanged", "changed", state, false);
        return checkBox;
    }

    private static FrameworkElement RenderSelect(PluginUiNode node, IReadOnlyDictionary<string, object?> state, IPluginUiHost host)
    {
        var comboBox = new ComboBox
        {
            IsEnabled = !PluginUiBinding.ResolveBoolean(GetProp(node, "Disabled"), state),
            MinWidth = 180
        };
        var selected = PluginUiBinding.ResolveText(GetProp(node, "Value"), state);
        foreach (var option in GetOptions(node))
        {
            var item = new ComboBoxItem { Content = option.Text, Tag = option.Value };
            comboBox.Items.Add(item);
            if (option.Value == selected)
                comboBox.SelectedItem = item;
        }

        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is ComboBoxItem { Tag: string value })
                Emit(host, node, "OnChanged", "changed", state, value);
        };
        return comboBox;
    }

    private static FrameworkElement RenderCard(PluginUiNode node, IReadOnlyDictionary<string, object?> state, IPluginUiHost host)
    {
        var content = new StackPanel { Margin = new Thickness(12) };
        var title = PluginUiBinding.ResolveText(GetProp(node, "Title"), state);
        if (!string.IsNullOrWhiteSpace(title))
            content.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });

        var description = PluginUiBinding.ResolveText(GetProp(node, "Description"), state);
        if (!string.IsNullOrWhiteSpace(description))
            content.Children.Add(new TextBlock { Text = description, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) });

        foreach (var child in node.Children)
        {
            var element = RenderNode(child, state, host);
            element.Margin = AddGap(element.Margin, "Spacing.Sm", state);
            content.Children.Add(element);
        }

        return new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0),
            Child = content
        };
    }

    private static FrameworkElement RenderTabs(PluginUiNode node, IReadOnlyDictionary<string, object?> state, IPluginUiHost host)
    {
        var tabs = new TabControl();
        var selected = PluginUiBinding.ResolveText(GetProp(node, "Value"), state);
        foreach (var item in GetTabItems(node))
        {
            var panel = new StackPanel { Margin = new Thickness(8) };
            foreach (var child in item.Children)
                panel.Children.Add(RenderNode(child, state, host));

            var tab = new TabItem
            {
                Header = item.Text,
                Tag = item.Value,
                Content = panel
            };
            tabs.Items.Add(tab);
            if (item.Value == selected)
                tabs.SelectedItem = tab;
        }

        tabs.SelectionChanged += (_, _) =>
        {
            if (tabs.SelectedItem is TabItem { Tag: string value })
                Emit(host, node, "OnChanged", "changed", state, value);
        };
        return tabs;
    }

    private static IReadOnlyList<PluginUiOption> GetOptions(PluginUiNode node)
    {
        return GetProp(node, "Options") as IReadOnlyList<PluginUiOption> ?? [];
    }

    private static IReadOnlyList<PluginUiTabItem> GetTabItems(PluginUiNode node)
    {
        return GetProp(node, "Items") as IReadOnlyList<PluginUiTabItem> ?? [];
    }

    private static object? GetProp(PluginUiNode node, string key)
    {
        return node.Props.TryGetValue(key, out var value) ? value : null;
    }

    private static void Emit(
        IPluginUiHost host,
        PluginUiNode node,
        string propName,
        string kind,
        IReadOnlyDictionary<string, object?> state,
        object? value = null)
    {
        var action = GetProp(node, propName);
        if (action is string handler && !string.IsNullOrWhiteSpace(handler))
        {
            host.Emit(new PluginUiEvent(handler, kind, value));
            return;
        }

        if (TryResolveCommand(action, state, value, out var command))
            host.Emit(new PluginUiEvent(null, kind, value, command));
    }

    private static bool TryResolveCommand(
        object? action,
        IReadOnlyDictionary<string, object?> state,
        object? eventValue,
        out PluginUiCommandEvent? command)
    {
        command = null;
        if (action is not IReadOnlyDictionary<string, object?> actionObject ||
            !actionObject.TryGetValue("Command", out var commandValue) ||
            commandValue is not string commandText ||
            string.IsNullOrWhiteSpace(commandText))
        {
            return false;
        }

        IReadOnlyDictionary<string, object?>? parameters = null;
        if (actionObject.TryGetValue("Params", out var parametersValue) && parametersValue is IReadOnlyDictionary<string, object?> parameterObject)
            parameters = ResolveCommandParams(parameterObject, state, eventValue);

        command = new PluginUiCommandEvent(commandText, parameters);
        return true;
    }

    private static IReadOnlyDictionary<string, object?> ResolveCommandParams(
        IReadOnlyDictionary<string, object?> parameters,
        IReadOnlyDictionary<string, object?> state,
        object? eventValue)
    {
        Dictionary<string, object?> resolved = [];
        foreach (var (key, parameterValue) in parameters)
        {
            if (IsUnsafeCommandParamKey(key))
                continue;
            resolved[key] = ResolveCommandParamValue(parameterValue, state, eventValue);
        }

        return new ReadOnlyDictionary<string, object?>(resolved);
    }

    private static object? ResolveCommandParamValue(
        object? value,
        IReadOnlyDictionary<string, object?> state,
        object? eventValue)
    {
        if (value is string text)
            return text == "{event.value}" ? eventValue : PluginUiBinding.ResolveValue(text, state);

        if (value is IReadOnlyDictionary<string, object?> dictionary)
            return ResolveCommandParams(dictionary, state, eventValue);

        if (value is IReadOnlyList<object?> list)
            return list.Select(item => ResolveCommandParamValue(item, state, eventValue)).ToArray();

        return value;
    }

    private static bool IsUnsafeCommandParamKey(string key) => key is "__proto__" or "prototype" or "constructor";

    private static FrameworkElement CollapsedPlaceholder() => new Border { Visibility = Visibility.Collapsed };

    private static string NonEmpty(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static Thickness ToButtonPadding(object? value, IReadOnlyDictionary<string, object?> state)
    {
        return PluginUiBinding.ResolveText(value, state) switch
        {
            "Xs" => new Thickness(6, 2, 6, 2),
            "Sm" => new Thickness(8, 4, 8, 4),
            "Lg" => new Thickness(16, 8, 16, 8),
            _ => new Thickness(12, 6, 12, 6)
        };
    }

    private static Thickness ToThickness(object? value, IReadOnlyDictionary<string, object?> state)
    {
        return Spacing(PluginUiBinding.ResolveText(value, state)) switch
        {
            0 => new Thickness(0),
            var spacing => new Thickness(spacing)
        };
    }

    private static Thickness AddGap(Thickness existing, object? value, IReadOnlyDictionary<string, object?> state)
    {
        var spacing = Spacing(PluginUiBinding.ResolveText(value, state));
        return new Thickness(existing.Left, existing.Top + spacing, existing.Right, existing.Bottom);
    }

    private static double Spacing(string value)
    {
        return value switch
        {
            "None" => 0,
            "Xs" or "Spacing.Xs" => 4,
            "Sm" or "Spacing.Sm" => 8,
            "Lg" or "Spacing.Lg" => 20,
            "Xl" or "Spacing.Xl" => 28,
            _ => 12
        };
    }
}
