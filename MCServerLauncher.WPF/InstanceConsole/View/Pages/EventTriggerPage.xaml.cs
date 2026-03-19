using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MCServerLauncher.Common.ProtoType.EventTrigger;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.Modules;

namespace MCServerLauncher.WPF.InstanceConsole.View.Pages
{
    /// <summary>
    ///    EventTriggerPage.xaml 的交互逻辑
    /// </summary>
    public partial class EventTriggerPage
    {
        public ObservableCollection<EventRule> Rules { get; set; } = new();

        public EventTriggerPage()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += EventTriggerPage_Loaded;
        }

        private async void EventTriggerPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (SettingsManager.Get?.App?.HideTips != null && 
                SettingsManager.Get.App.HideTips.TryGetValue("EventTriggerMultiSelect", out var hide) && hide)
            {
                MultiSelectTipBar.IsOpen = false;
            }
            await LoadRulesAsync();
        }

        private void MultiSelectTipBar_DoNotShowAgain_Click(iNKORE.UI.WPF.Modern.Controls.InfoBar sender, object args)
        {
            if (SettingsManager.Get?.App != null)
            {
                if (SettingsManager.Get.App.HideTips == null)
                {
                    SettingsManager.Get.App.HideTips = new System.Collections.Generic.Dictionary<string, bool>();
                }
                SettingsManager.Get.App.HideTips["EventTriggerMultiSelect"] = true;
                SettingsManager.SaveSetting("App.HideTips", SettingsManager.Get.App.HideTips);
            }
        }

        private async System.Threading.Tasks.Task LoadRulesAsync()
        {
            try
            {
                if (!InstanceDataManager.Instance.IsConnected)
                {
                    Notification.Push(Lang.Tr["Error"], Lang.Tr["FuncDisabledReason_NoDaemon"], true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
                    return;
                }

                var rules = await InstanceDataManager.Instance.GetEventRulesAsync();
                Rules.Clear();
                foreach (var rule in rules)
                {
                    Rules.Add(rule);
                }
            }
            catch (Exception ex)
            {
                Notification.Push(Lang.Tr["Error"], string.Format(Lang.Tr["EventTrigger_LoadRulesFailed"], ex.Message), true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
            }
        }

        private void AddRule_Click(object sender, RoutedEventArgs e)
        {
            var newRule = new EventRule
            {
                Name = Lang.Tr["EventTrigger_NewRuleName"],
                Description = Lang.Tr["EventTrigger_NewRuleDescription"]
            };
            Rules.Add(newRule);
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!InstanceDataManager.Instance.IsConnected)
                {
                    Notification.Push(Lang.Tr["Error"], Lang.Tr["FuncDisabledReason_NoDaemon"], true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
                    return;
                }

                await InstanceDataManager.Instance.SaveEventRulesAsync(Rules.ToList());
                Notification.Push(Lang.Tr["Success"], Lang.Tr["EventTrigger_SaveRulesSuccess"], true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                Notification.Push(Lang.Tr["Error"], string.Format(Lang.Tr["EventTrigger_SaveRulesFailed"], ex.Message), true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadRulesAsync();
        }

        private void DeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is EventRule rule)
            {
                Rules.Remove(rule);
            }
        }

        private void CopyRule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is EventRule rule)
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(rule, MCServerLauncher.Common.ProtoType.JsonSettings.Settings);
                var newRule = Newtonsoft.Json.JsonConvert.DeserializeObject<EventRule>(json, MCServerLauncher.Common.ProtoType.JsonSettings.Settings);
                if (newRule != null)
                {
                    newRule.Id = Guid.NewGuid();
                    
                    var baseName = rule.Name;
                    var copySuffix = " - Copy";
                    var newName = baseName + copySuffix;
                    var copyCount = 1;

                    while (Rules.Any(r => r.Name == newName))
                    {
                        copyCount++;
                        newName = $"{baseName}{copySuffix} ({copyCount})";
                    }
                    
                    newRule.Name = newName;
                    
                    foreach (var trigger in newRule.Triggers) trigger.Id = Guid.NewGuid();
                    foreach (var ruleset in newRule.Rulesets) ruleset.Id = Guid.NewGuid();
                    foreach (var action in newRule.Actions) action.Id = Guid.NewGuid();
                    
                    Rules.Add(newRule);
                }
            }
        }

        private void EditRule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is EventRule rule)
            {
                var dialog = new Dialogs.EventRuleEditorDialog(rule);
                dialog.ShowDialog();
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = Lang.Tr["Import"]
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var json = System.IO.File.ReadAllText(dialog.FileName);
                    var rules = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<EventRule>>(json, MCServerLauncher.Common.ProtoType.JsonSettings.Settings);
                    if (rules != null)
                    {
                        foreach (var rule in rules)
                        {
                            // Generate new IDs to avoid conflicts
                            rule.Id = Guid.NewGuid();
                            foreach (var trigger in rule.Triggers) trigger.Id = Guid.NewGuid();
                            foreach (var ruleset in rule.Rulesets) ruleset.Id = Guid.NewGuid();
                            foreach (var action in rule.Actions) action.Id = Guid.NewGuid();
                            
                            Rules.Add(rule);
                        }
                        Notification.Push(Lang.Tr["Success"], Lang.Tr["Success"], true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success);
                    }
                }
                catch (Exception ex)
                {
                    Notification.Push(Lang.Tr["Error"], string.Format(Lang.Tr["EventTrigger_LoadRulesFailed"], ex.Message), true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
                }
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = Lang.Tr["Export"],
                FileName = "EventRules.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var rulesToExport = RulesListView.SelectedItems.Count > 0 
                        ? RulesListView.SelectedItems.Cast<EventRule>().ToList() 
                        : Rules.ToList();

                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(rulesToExport, Newtonsoft.Json.Formatting.Indented, MCServerLauncher.Common.ProtoType.JsonSettings.Settings);
                    System.IO.File.WriteAllText(dialog.FileName, json);
                    Notification.Push(Lang.Tr["Success"], Lang.Tr["Success"], true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success);
                }
                catch (Exception ex)
                {
                    Notification.Push(Lang.Tr["Error"], string.Format(Lang.Tr["EventTrigger_SaveRulesFailed"], ex.Message), true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
                }
            }
        }
    }
}
