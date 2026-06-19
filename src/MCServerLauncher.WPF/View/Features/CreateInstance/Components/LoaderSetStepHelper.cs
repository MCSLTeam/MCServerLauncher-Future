using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    internal static class LoaderSetStepHelper
    {
        public static bool HasSelection(ComboBox comboBox)
        {
            return comboBox.SelectedIndex >= 0 && comboBox.SelectedItem is not null;
        }

        public static List<string> NonEmptyStrings(IEnumerable<string?> values)
        {
            var result = new List<string>();
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);
            }

            return result;
        }

        public static void BindSelectionStatus(
            DependencyObject owner,
            ComboBox comboBox,
            DependencyProperty statusProperty)
        {
            void UpdateStatus(object? sender, SelectionChangedEventArgs args)
            {
                owner.SetValue(statusProperty, HasSelection(comboBox));
            }

            comboBox.SelectionChanged += UpdateStatus;
            MCServerLauncher.WPF.Modules.VisualTreeHelper.InitStepState(comboBox);
            owner.SetValue(statusProperty, HasSelection(comboBox));
        }

        public static PropertyChangedCallback CreateStatusVisibilityCallback<TControl>(
            Func<TControl, UIElement> statusElementSelector)
            where TControl : DependencyObject
        {
            return (d, e) =>
            {
                if (d is not TControl control) return;
                if (e.NewValue is not bool status) return;

                statusElementSelector(control).Visibility = status switch
                {
                    true => Visibility.Visible,
                    false => Visibility.Hidden,
                };
            };
        }

        public static CreateInstanceData CreateLoaderVersionData(
            ComboBox minecraftVersionComboBox,
            ComboBox loaderVersionComboBox,
            string loaderName)
        {
            var mcVersion = minecraftVersionComboBox.SelectedItem?.ToString();
            var loaderVersion = loaderVersionComboBox.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(mcVersion) || string.IsNullOrWhiteSpace(loaderVersion))
                throw new InvalidOperationException($"Minecraft and {loaderName} versions must be selected.");

            return new CreateInstanceData
            {
                Type = CreateInstanceDataType.Struct,
                Data = new MinecraftLoaderVersion
                {
                    MCVersion = mcVersion,
                    LoaderVersion = loaderVersion,
                }
            };
        }
    }
}
