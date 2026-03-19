using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Globalization;
using System;
using MCServerLauncher.Common.ProtoType.EventTrigger;

namespace MCServerLauncher.WPF.InstanceConsole.View.Dialogs
{
    public partial class EventRuleEditorDialog : System.Windows.Window
    {
        public EventRule Rule { get; }
        public ObservableCollection<object> Triggers { get; } = new();
        public ObservableCollection<object> Rulesets { get; } = new();
        public ObservableCollection<object> Actions { get; } = new();

        public EventRuleEditorDialog(EventRule rule)
        {
            InitializeComponent();
            Rule = rule;
            
            // Load existing triggers and actions into observable collections
            foreach (var trigger in rule.Triggers)
            {
                Triggers.Add(WrapTrigger(trigger));
            }
            
            foreach (var ruleset in rule.Rulesets)
            {
                Rulesets.Add(WrapRuleset(ruleset));
            }
            
            foreach (var action in rule.Actions)
            {
                Actions.Add(WrapAction(action));
            }

            DataContext = this;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Save back to Rule
            Rule.Triggers = Triggers.Select(UnwrapTrigger).ToList();
            Rule.Rulesets = Rulesets.Select(UnwrapRuleset).ToList();
            Rule.Actions = Actions.Select(UnwrapAction).ToList();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // --- Triggers ---

        private void AddTrigger_Click(object sender, RoutedEventArgs e)
        {
            // Handled by Flyout
        }

        private void AddConsoleOutputTrigger_Click(object sender, RoutedEventArgs e)
        {
            Triggers.Add(new ConsoleOutputTriggerWrapper(new ConsoleOutputTrigger()));
        }

        private void AddScheduleTrigger_Click(object sender, RoutedEventArgs e)
        {
            Triggers.Add(new ScheduleTriggerWrapper(new ScheduleTrigger()));
        }

        private void AddInstanceStatusTrigger_Click(object sender, RoutedEventArgs e)
        {
            Triggers.Add(new InstanceStatusTriggerWrapper(new InstanceStatusTrigger()));
        }

        private void DeleteTrigger_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag != null)
            {
                Triggers.Remove(button.Tag);
            }
        }

        // --- Rulesets ---

        private void AddAlwaysTrueRuleset_Click(object sender, RoutedEventArgs e)
        {
            Rulesets.Add(new AlwaysTrueRulesetWrapper(new AlwaysTrueRuleset()));
        }

        private void AddAlwaysFalseRuleset_Click(object sender, RoutedEventArgs e)
        {
            Rulesets.Add(new AlwaysFalseRulesetWrapper(new AlwaysFalseRuleset()));
        }

        private void AddInstanceStatusRuleset_Click(object sender, RoutedEventArgs e)
        {
            Rulesets.Add(new InstanceStatusRulesetWrapper(new InstanceStatusRuleset()));
        }

        private void DeleteRuleset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag != null)
            {
                Rulesets.Remove(button.Tag);
            }
        }

        // --- Actions ---

        private void AddAction_Click(object sender, RoutedEventArgs e)
        {
            // Handled by Flyout
        }

        private void AddSendCommandAction_Click(object sender, RoutedEventArgs e)
        {
            Actions.Add(new SendCommandActionWrapper(new SendCommandAction()));
        }

        private void AddChangeInstanceStatusAction_Click(object sender, RoutedEventArgs e)
        {
            Actions.Add(new ChangeInstanceStatusActionWrapper(new ChangeInstanceStatusAction()));
        }

        private void AddSendNotificationAction_Click(object sender, RoutedEventArgs e)
        {
            Actions.Add(new SendNotificationActionWrapper(new SendNotificationAction()));
        }

        private void DeleteAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag != null)
            {
                Actions.Remove(button.Tag);
            }
        }

        // --- Wrappers for DataTemplates ---

        private object WrapTrigger(TriggerDefinition trigger)
        {
            return trigger switch
            {
                ConsoleOutputTrigger t => new ConsoleOutputTriggerWrapper(t),
                ScheduleTrigger t => new ScheduleTriggerWrapper(t),
                InstanceStatusTrigger t => new InstanceStatusTriggerWrapper(t),
                _ => trigger
            };
        }

        private TriggerDefinition UnwrapTrigger(object wrapper)
        {
            return wrapper switch
            {
                ConsoleOutputTriggerWrapper w => w.Trigger,
                ScheduleTriggerWrapper w => w.Trigger,
                InstanceStatusTriggerWrapper w => w.Trigger,
                TriggerDefinition t => t,
                _ => null
            };
        }

        private object WrapRuleset(RulesetDefinition ruleset)
        {
            return ruleset switch
            {
                AlwaysTrueRuleset r => new AlwaysTrueRulesetWrapper(r),
                AlwaysFalseRuleset r => new AlwaysFalseRulesetWrapper(r),
                InstanceStatusRuleset r => new InstanceStatusRulesetWrapper(r),
                _ => ruleset
            };
        }

        private RulesetDefinition UnwrapRuleset(object wrapper)
        {
            return wrapper switch
            {
                AlwaysTrueRulesetWrapper w => w.Ruleset,
                AlwaysFalseRulesetWrapper w => w.Ruleset,
                InstanceStatusRulesetWrapper w => w.Ruleset,
                RulesetDefinition r => r,
                _ => null
            };
        }

        private object WrapAction(ActionDefinition action)
        {
            return action switch
            {
                SendCommandAction a => new SendCommandActionWrapper(a),
                ChangeInstanceStatusAction a => new ChangeInstanceStatusActionWrapper(a),
                SendNotificationAction a => new SendNotificationActionWrapper(a),
                _ => action
            };
        }

        private ActionDefinition UnwrapAction(object wrapper)
        {
            return wrapper switch
            {
                SendCommandActionWrapper w => w.Action,
                ChangeInstanceStatusActionWrapper w => w.Action,
                SendNotificationActionWrapper w => w.Action,
                ActionDefinition a => a,
                _ => null
            };
        }
    }

    // Wrapper classes to allow DataTemplates to match based on type
    public class ConsoleOutputTriggerWrapper
    {
        public ConsoleOutputTrigger Trigger { get; }
        public ConsoleOutputTriggerWrapper(ConsoleOutputTrigger trigger) => Trigger = trigger;
        public string Pattern { get => Trigger.Pattern; set => Trigger.Pattern = value ?? string.Empty; }
        public bool IsRegex { get => Trigger.IsRegex; set => Trigger.IsRegex = value; }
    }

    public class ScheduleTriggerWrapper
    {
        public ScheduleTrigger Trigger { get; }
        public ScheduleTriggerWrapper(ScheduleTrigger trigger) => Trigger = trigger;
        public string CronExpression { get => Trigger.CronExpression; set => Trigger.CronExpression = value ?? string.Empty; }
    }

    public class InstanceStatusTriggerWrapper
    {
        public InstanceStatusTrigger Trigger { get; }
        public InstanceStatusTriggerWrapper(InstanceStatusTrigger trigger) => Trigger = trigger;
        public string TargetStatus { get => Trigger.TargetStatus; set => Trigger.TargetStatus = value ?? string.Empty; }
    }

    public class AlwaysTrueRulesetWrapper
    {
        public AlwaysTrueRuleset Ruleset { get; }
        public AlwaysTrueRulesetWrapper(AlwaysTrueRuleset ruleset) => Ruleset = ruleset;
    }

    public class AlwaysFalseRulesetWrapper
    {
        public AlwaysFalseRuleset Ruleset { get; }
        public AlwaysFalseRulesetWrapper(AlwaysFalseRuleset ruleset) => Ruleset = ruleset;
    }

    public class InstanceStatusRulesetWrapper
    {
        public InstanceStatusRuleset Ruleset { get; }
        public InstanceStatusRulesetWrapper(InstanceStatusRuleset ruleset) => Ruleset = ruleset;
        public string TargetStatus { get => Ruleset.TargetStatus; set => Ruleset.TargetStatus = value ?? string.Empty; }
    }

    public class SendCommandActionWrapper
    {
        public SendCommandAction Action { get; }
        public SendCommandActionWrapper(SendCommandAction action) => Action = action;
        public string Command { get => Action.Command; set => Action.Command = value ?? string.Empty; }
    }

    public class ChangeInstanceStatusActionWrapper
    {
        public ChangeInstanceStatusAction Action { get; }
        public ChangeInstanceStatusActionWrapper(ChangeInstanceStatusAction action) => Action = action;
        public string ActionType { get => Action.Action; set => Action.Action = value ?? string.Empty; }
    }

    public class SendNotificationActionWrapper
    {
        public SendNotificationAction Action { get; }
        public SendNotificationActionWrapper(SendNotificationAction action) => Action = action;
        public string Title { get => Action.Title; set => Action.Title = value ?? string.Empty; }
        public string Message { get => Action.Message; set => Action.Message = value ?? string.Empty; }
        public string Severity { get => Action.Severity; set => Action.Severity = value ?? "Info"; }
    }

    public class ActionExecutionModeToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string mode)
            {
                return mode.Equals("Parallel", StringComparison.OrdinalIgnoreCase) ? "同时" : "然后";
            }
            return "然后";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
