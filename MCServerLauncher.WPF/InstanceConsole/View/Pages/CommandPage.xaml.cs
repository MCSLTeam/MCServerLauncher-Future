using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.InstanceConsole.View.Dialogs;
using MCServerLauncher.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;

namespace MCServerLauncher.WPF.InstanceConsole.View.Pages
{
    public partial class CommandPage
    {
        private static bool isFullscreen = false;
        private bool _isPageLoaded = false;
        private readonly CommandPageViewModel _viewModel;

        public CommandPage()
        {
            InitializeComponent();
            _viewModel = App.Services.GetRequiredService<CommandPageViewModel>();
            DataContext = _viewModel;
            OnFullscreenButtonContent.Visibility = Visibility.Visible;
            OffFullscreenButtonContent.Visibility = Visibility.Collapsed;
            InitializeSyntaxHighlighting();
        }

        private void InitializeSyntaxHighlighting()
        {
            try
            {
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
                            Log.Error("[CommandPage] Could not find embedded resource '{0}'", resourceName);
                        }
                    }
                }

                var highlighting = HighlightingManager.Instance.GetDefinition("Log");
                if (highlighting != null)
                {
                    FixHighlightingColors(highlighting);
                    ConsoleLogEditor.SyntaxHighlighting = highlighting;
                }
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
            var visited = new HashSet<HighlightingRuleSet>();
            foreach (var color in definition.NamedHighlightingColors) FixColor(color);
            FixRuleSet(definition.MainRuleSet, visited);
        }

        private void FixRuleSet(HighlightingRuleSet ruleSet, HashSet<HighlightingRuleSet> visited)
        {
            if (ruleSet == null || visited.Contains(ruleSet)) return;
            visited.Add(ruleSet);
            foreach (var rule in ruleSet.Rules) FixColor(rule.Color);
            foreach (var span in ruleSet.Spans)
            {
                FixColor(span.StartColor);
                FixColor(span.EndColor);
                FixRuleSet(span.RuleSet, visited);
            }
        }

        private void FixColor(HighlightingColor color)
        {
            if (color?.Foreground is SimpleHighlightingBrush simpleBrush && simpleBrush.GetBrush(null) is SolidColorBrush solidBrush)
                color.Foreground = new ThemeAwareHighlightingBrush(solidBrush.Color);
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isPageLoaded)
            {
                _isPageLoaded = true;
                InstanceDataManager.Instance.LogReceived += OnLogReceived;
                CommandInputTextBox.Focus();

                try
                {
                    var history = await InstanceDataManager.Instance.GetInstanceLogHistoryAsync();
                    if (history is { Length: > 0 })
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
                _viewModel.Dispose();
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

        private async void CommandInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                await _viewModel.SendCommandCommand.ExecuteAsync(null);
        }

        private void ToggleFullscreen(object sender, RoutedEventArgs e)
        {
            var mainWindow = System.Windows.Window.GetWindow(this) as Window;
            if (mainWindow == null) return;

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
