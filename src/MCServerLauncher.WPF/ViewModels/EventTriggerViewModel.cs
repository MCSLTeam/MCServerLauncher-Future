using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;

namespace MCServerLauncher.WPF.ViewModels;

public partial class EventTriggerViewModel : ObservableObject
{
    private readonly INotificationService _notification;

    public ObservableCollection<EventRule> Rules { get; } = new();

    public EventTriggerViewModel(INotificationService notification)
    {
        _notification = notification;
    }

    [RelayCommand]
    private async Task LoadRulesAsync()
    {
        try
        {
            if (!InstanceDataManager.Instance.IsConnected)
            {
                _notification.Push(Lang.Tr["Error"], Lang.Tr["FuncDisabledReason_NoDaemon"], true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
                return;
            }

            var rules = await InstanceDataManager.Instance.GetEventRulesAsync();
            Rules.Clear();
            foreach (var rule in rules)
                Rules.Add(rule);
        }
        catch (Exception ex)
        {
            _notification.Push(Lang.Tr["Error"], string.Format(Lang.Tr["EventTrigger_LoadRulesFailed"], ex.Message), true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task SaveRulesAsync()
    {
        try
        {
            if (!InstanceDataManager.Instance.IsConnected)
            {
                _notification.Push(Lang.Tr["Error"], Lang.Tr["FuncDisabledReason_NoDaemon"], true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
                return;
            }

            await InstanceDataManager.Instance.SaveEventRulesAsync(Rules.ToList());
            _notification.Push(Lang.Tr["Success"], Lang.Tr["EventTrigger_SaveRulesSuccess"], true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            _notification.Push(Lang.Tr["Error"], string.Format(Lang.Tr["EventTrigger_SaveRulesFailed"], ex.Message), true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private void AddRule()
    {
        Rules.Add(new EventRule
        {
            Name = Lang.Tr["EventTrigger_NewRuleName"],
            Description = Lang.Tr["EventTrigger_NewRuleDescription"]
        });
    }

    [RelayCommand]
    private void DeleteRule(EventRule rule)
    {
        Rules.Remove(rule);
    }

    [RelayCommand]
    private void CopyRule(EventRule rule)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(rule, StjResolver.CreateDefaultOptions());
        var newRule = System.Text.Json.JsonSerializer.Deserialize<EventRule>(json, StjResolver.CreateDefaultOptions());
        if (newRule == null) return;

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

    [RelayCommand]
    private void ImportRules()
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
                var rules = System.Text.Json.JsonSerializer.Deserialize<List<EventRule>>(json, StjResolver.CreateDefaultOptions());
                if (rules != null)
                {
                    foreach (var rule in rules)
                    {
                        rule.Id = Guid.NewGuid();
                        foreach (var trigger in rule.Triggers) trigger.Id = Guid.NewGuid();
                        foreach (var ruleset in rule.Rulesets) ruleset.Id = Guid.NewGuid();
                        foreach (var action in rule.Actions) action.Id = Guid.NewGuid();
                        Rules.Add(rule);
                    }
                    _notification.Push(Lang.Tr["Success"], Lang.Tr["Success"], true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success);
                }
            }
            catch (Exception ex)
            {
                _notification.Push(Lang.Tr["Error"], string.Format(Lang.Tr["EventTrigger_LoadRulesFailed"], ex.Message), true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
            }
        }
    }

    public void ExportRules(IEnumerable<EventRule>? selectedRules)
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
                var rulesToExport = selectedRules?.ToList() ?? Rules.ToList();
                var json = System.Text.Json.JsonSerializer.Serialize(rulesToExport, new System.Text.Json.JsonSerializerOptions(StjResolver.CreateDefaultOptions()) { WriteIndented = true });
                System.IO.File.WriteAllText(dialog.FileName, json);
                _notification.Push(Lang.Tr["Success"], Lang.Tr["Success"], true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                _notification.Push(Lang.Tr["Error"], string.Format(Lang.Tr["EventTrigger_SaveRulesFailed"], ex.Message), true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
            }
        }
    }
}
