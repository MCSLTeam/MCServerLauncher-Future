using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.InstanceConsole.View.Dialogs;
using MCServerLauncher.WPF.Modules;
using Serilog;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;

namespace MCServerLauncher.WPF.InstanceConsole.View.Pages
{
    /// <summary>
    ///    CommandPage.xaml 的交互逻辑
    /// </summary>
    public partial class CommandPage
    {
        private static bool isFullscreen = false;
        private bool _isPageLoaded = false;

        public CommandPage()
        {
            InitializeComponent();
            OnFullscreenButtonContent.Visibility = Visibility.Visible;
            OffFullscreenButtonContent.Visibility = Visibility.Collapsed;
            
            InitializeSyntaxHighlighting();
        }

        private void InitializeSyntaxHighlighting()
        {
            try
            {
                // Register Log syntax highlighting if not already registered
                if (HighlightingManager.Instance.GetDefinition("Log") == null)
                {
                    var resourceName = "MCServerLauncher.WPF.Resources.SyntaxHighlighting.Log.xshd";
                    using (var stream = typeof(FileEditorWindow).Assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream != null)
                        {
                            using (var reader = new XmlTextReader(stream))
                            {
                                var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                                HighlightingManager.Instance.RegisterHighlighting("Log", new[] { ".log", ".txt" }, definition);
                            }
                        }
                        else
                        {
                            Log.Error("[CommandPage] Could not find embedded resource '{0}'. Available resources: {1}", 
                                resourceName, 
                                string.Join(", ", typeof(FileEditorWindow).Assembly.GetManifestResourceNames()));
                        }
                    }
                }

                var highlighting = HighlightingManager.Instance.GetDefinition("Log");
                if (highlighting != null)
                {
                    // Apply theme-aware colors
                    FixHighlightingColors(highlighting);
                    ConsoleLogEditor.SyntaxHighlighting = highlighting;
                }
                
                // Hide cursor
                ConsoleLogEditor.TextArea.Caret.Hide();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CommandPage] Failed to initialize syntax highlighting");
            }
        }

        private void FixHighlightingColors(IHighlightingDefinition definition)
        {
            if (definition == null) return;

            var visitedRuleSets = new System.Collections.Generic.HashSet<HighlightingRuleSet>();

            foreach (var color in definition.NamedHighlightingColors)
            {
                FixColor(color);
            }

            FixRuleSet(definition.MainRuleSet, visitedRuleSets);
        }

        private void FixRuleSet(HighlightingRuleSet ruleSet, System.Collections.Generic.HashSet<HighlightingRuleSet> visited)
        {
            if (ruleSet == null || visited.Contains(ruleSet)) return;
            visited.Add(ruleSet);

            foreach (var rule in ruleSet.Rules)
            {
                FixColor(rule.Color);
            }

            foreach (var span in ruleSet.Spans)
            {
                FixColor(span.StartColor);
                FixColor(span.EndColor);
                FixRuleSet(span.RuleSet, visited);
            }
        }

        private void FixColor(HighlightingColor color)
        {
            if (color == null) return;
            if (color.Foreground is SimpleHighlightingBrush simpleBrush && simpleBrush.GetBrush(null) is SolidColorBrush solidBrush)
            {
                color.Foreground = new ThemeAwareHighlightingBrush(solidBrush.Color);
            }
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isPageLoaded)
            {
                _isPageLoaded = true;
                // Subscribe to log events
                InstanceDataManager.Instance.LogReceived += OnLogReceived;
                CommandInputTextBox.Focus();

                // Load log history
                try
                {
                    var history = await InstanceDataManager.Instance.GetInstanceLogHistoryAsync();
                    if (history != null && history.Length > 0)
                    {
                        ConsoleLogEditor.AppendText(string.Join(Environment.NewLine, history) + Environment.NewLine);
                        ConsoleLogEditor.ScrollToEnd();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[CommandPage] Failed to load log history");
                }
            }
        }

        public Task DisposeAsync()
        {
            if (_isPageLoaded)
            {
                InstanceDataManager.Instance.LogReceived -= OnLogReceived;
                _isPageLoaded = false;
            }
            return Task.CompletedTask;
        }

        private void OnLogReceived(object? sender, string logMessage)
        {
            Dispatcher.Invoke(() =>
            {
                ConsoleLogEditor.AppendText(logMessage + Environment.NewLine);
                ConsoleLogEditor.ScrollToEnd();
            });
        }

        private async void SendCommand_Click(object sender, RoutedEventArgs e)
        {
            await SendCommandAsync();
        }

        private async void CommandInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await SendCommandAsync();
            }
        }

        private async Task SendCommandAsync()
        {
            var command = CommandInputTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(command))
                return;

            try
            {
                await InstanceDataManager.Instance.SendCommandAsync(command);
                CommandInputTextBox.Clear();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CommandPage] Failed to send command: {0}", command);
                Notification.Push(
                    Lang.Tr["Error"],
                    string.Format(Lang.Tr["SendCommandFailed"], ex.Message),
                    true,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error
                );
            }
        }

        private async void StartInstance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await InstanceDataManager.Instance.StartInstanceAsync();
                Notification.Push(
                    Lang.Tr["Success"],
                    Lang.Tr["StartCommandSentSuccess"],
                    false,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success
                );
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CommandPage] Failed to start instance");
                Notification.Push(
                    Lang.Tr["Error"],
                    string.Format(Lang.Tr["InstanceCard_StartFailed"], ex.Message),
                    true,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error
                );
            }
        }

        private async void StopInstance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await InstanceDataManager.Instance.StopInstanceAsync();
                Notification.Push(
                    Lang.Tr["Success"],
                    Lang.Tr["StopCommandSentSuccess"],
                    false,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success
                );
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CommandPage] Failed to stop instance");
                Notification.Push(
                    Lang.Tr["Error"],
                    string.Format(Lang.Tr["InstanceCard_StopFailed"], ex.Message),
                    true,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error
                );
            }
        }

        private async void KillInstance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await InstanceDataManager.Instance.KillInstanceAsync();
                Notification.Push(
                    Lang.Tr["Success"],
                    Lang.Tr["KillCommandSentSuccess"],
                    false,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success
                );
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CommandPage] Failed to kill instance");
                Notification.Push(
                    Lang.Tr["Error"],
                    string.Format(Lang.Tr["InstanceCard_KillFailed"], ex.Message),
                    true,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error
                );
            }
        }

        private async void RestartInstance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await InstanceDataManager.Instance.RestartInstanceAsync();
                Notification.Push(
                    Lang.Tr["Success"],
                    Lang.Tr["RestartCommandSentSuccess"],
                    false,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success
                );
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CommandPage] Failed to restart instance");
                Notification.Push(
                    Lang.Tr["Error"],
                    string.Format(Lang.Tr["InstanceCard_RestartFailed"], ex.Message),
                    true,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error
                );
            }
        }

        private void ToggleFullscreen(object sender, RoutedEventArgs e)
        {
            var mainWindow = Window.GetWindow(this) as InstanceConsole.Window;
            if (mainWindow != null)
            {
                if (!isFullscreen)
                {
                    mainWindow.WindowStyle = WindowStyle.None;
                    mainWindow.ResizeMode = ResizeMode.NoResize;
                    mainWindow.WindowState = WindowState.Maximized;
                    mainWindow.Topmost = true;
                    isFullscreen = true;
                    OnFullscreenButtonContent.Visibility = Visibility.Collapsed;
                    OffFullscreenButtonContent.Visibility = Visibility.Visible;
                }
                else
                {
                    mainWindow.WindowStyle = WindowStyle.SingleBorderWindow;
                    mainWindow.ResizeMode = ResizeMode.CanResize;
                    mainWindow.WindowState = WindowState.Normal;
                    mainWindow.Topmost = false;
                    isFullscreen = false;
                    OnFullscreenButtonContent.Visibility = Visibility.Visible;
                    OffFullscreenButtonContent.Visibility = Visibility.Collapsed;
                }
                mainWindow.Show();
            }
        }
    }
}