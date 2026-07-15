using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.WPF.InstanceConsole.View.Dialogs;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MCServerLauncher.WPF.InstanceConsole.View.Pages
{
    public partial class EventTriggerPage
    {
        private readonly EventTriggerViewModel _viewModel;

        public EventTriggerPage()
        {
            InitializeComponent();
            _viewModel = App.Services.GetRequiredService<EventTriggerViewModel>();
            DataContext = _viewModel;
            Loaded += EventTriggerPage_Loaded;
        }

        private async void EventTriggerPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (SettingsManager.Get?.App?.HideTips != null &&
                SettingsManager.Get.App.HideTips.TryGetValue("EventTriggerMultiSelect", out var hide) && hide)
            {
                MultiSelectTipBar.IsOpen = false;
            }
            await _viewModel.LoadRulesCommand.ExecuteAsync(null);
        }

        private void MultiSelectTipBar_DoNotShowAgain_Click(iNKORE.UI.WPF.Modern.Controls.InfoBar sender, object args)
        {
            if (SettingsManager.Get?.App != null)
            {
                SettingsManager.Get.App.HideTips ??= new Dictionary<string, bool>();
                SettingsManager.Get.App.HideTips["EventTriggerMultiSelect"] = true;
                SettingsManager.SaveSetting("App.HideTips", SettingsManager.Get.App.HideTips);
            }
        }

        private void EditRule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is EventRule rule)
            {
                var dialog = new EventRuleEditorDialog(rule);
                dialog.ShowDialog();
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            IEnumerable<EventRule>? selected = RulesListView.SelectedItems.Count > 0
                ? RulesListView.SelectedItems.Cast<EventRule>()
                : null;
            _viewModel.ExportRules(selected);
        }
    }
}
