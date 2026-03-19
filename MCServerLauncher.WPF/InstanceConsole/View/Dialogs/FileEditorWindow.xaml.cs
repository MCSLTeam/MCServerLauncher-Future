using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.InstanceConsole.View.Pages;
using MCServerLauncher.WPF.Modules;
using Serilog;

namespace MCServerLauncher.WPF.InstanceConsole.View.Dialogs
{
    public partial class FileEditorWindow : System.Windows.Window
    {
        public FileEditorWindow()
        {
            InitializeComponent();
        }
    }

    public class FileEditorViewModel : INotifyPropertyChanged
    {
        private readonly IDaemon _daemon;
        private readonly string _realPath;
        private readonly string _virtualPath;
        private readonly long _fileSize;
        private readonly System.Windows.Window _window;
        
        private TextDocument _document = new TextDocument();
        private bool _isLoading;
        private string _loadingText = Lang.Tr["Loading"];
        private string _statusText = Lang.Tr["Ready"];
        private EncodingInfo _selectedEncoding;
        private IHighlightingDefinition _selectedSyntaxHighlighting;
        private bool _isDirty;
        private bool _isClosing;

        private double _fontSize = 14;
        private bool _wordWrap = true;
        private bool _showLineNumbers = true;
        private bool _showStatusBar = true;

        public double FontSize
        {
            get => _fontSize;
            set { _fontSize = value; OnPropertyChanged(); }
        }

        public bool WordWrap
        {
            get => _wordWrap;
            set { _wordWrap = value; OnPropertyChanged(); }
        }

        public bool ShowLineNumbers
        {
            get => _showLineNumbers;
            set { _showLineNumbers = value; OnPropertyChanged(); }
        }

        public bool ShowStatusBar
        {
            get => _showStatusBar;
            set { _showStatusBar = value; OnPropertyChanged(); }
        }

        public TextDocument Document
        {
            get => _document;
            set { _document = value; OnPropertyChanged(); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public string LoadingText
        {
            get => _loadingText;
            set { _loadingText = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public string FilePath => _virtualPath;

        public ObservableCollection<EncodingInfo> Encodings { get; }
        public EncodingInfo SelectedEncoding
        {
            get => _selectedEncoding;
            set
            {
                if (_selectedEncoding != value)
                {
                    _selectedEncoding = value;
                    OnPropertyChanged();
                    if (!_isLoading)
                    {
                        _ = ReloadFileAsync();
                    }
                }
            }
        }

        public IHighlightingDefinition SelectedSyntaxHighlighting
        {
            get => _selectedSyntaxHighlighting;
            set { _selectedSyntaxHighlighting = value; OnPropertyChanged(); }
        }

        public ICommand SaveCommand { get; }
        public ICommand ReloadCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand InsertTimeDateCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand RestoreDefaultZoomCommand { get; }
        public ICommand ChangeEncodingCommand { get; }

        public FileEditorViewModel(IDaemon daemon, string realPath, string virtualPath, long fileSize, System.Windows.Window window)
        {
            _daemon = daemon;
            _realPath = realPath;
            _virtualPath = virtualPath;
            _fileSize = fileSize;
            _window = window;

            Encodings = new ObservableCollection<EncodingInfo>(Encoding.GetEncodings().OrderBy(e => e.DisplayName));
            _selectedEncoding = Encodings.FirstOrDefault(e => e.CodePage == Encoding.UTF8.CodePage) ?? Encodings.First();

            RegisterCustomHighlighting();

            _selectedSyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(virtualPath)) 
                                          ?? HighlightingManager.Instance.GetDefinition("Text");

            FixHighlightingColors(_selectedSyntaxHighlighting);

            SaveCommand = new RelayCommand(async _ => await SaveFileAsync());
            ReloadCommand = new RelayCommand(async _ => await ReloadFileAsync());
            ExitCommand = new RelayCommand(_ => _window.Close());
            InsertTimeDateCommand = new RelayCommand(o => 
            {
                if (o is TextEditor editor)
                {
                    editor.Document.Insert(editor.CaretOffset, DateTime.Now.ToString());
                }
            });
            ZoomInCommand = new RelayCommand(_ => FontSize = Math.Min(FontSize + 2, 72));
            ZoomOutCommand = new RelayCommand(_ => FontSize = Math.Max(FontSize - 2, 6));
            RestoreDefaultZoomCommand = new RelayCommand(_ => FontSize = 14);
            ChangeEncodingCommand = new RelayCommand(async _ => await ShowEncodingDialogAsync());

            Document.TextChanged += (s, e) => 
            {
                if (!_isLoading)
                {
                    _isDirty = true;
                    StatusText = Lang.Tr["Modified"];
                }
            };

            _window.Closing += Window_Closing;
        }

        private async Task ShowEncodingDialogAsync()
        {
            var comboBox = new ComboBox
            {
                ItemsSource = Encodings,
                SelectedItem = SelectedEncoding,
                DisplayMemberPath = "DisplayName",
                Width = 300,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var stackPanel = new StackPanel();
            stackPanel.Children.Add(new TextBlock { Text = Lang.Tr["PreventGarbageTextTip"], TextWrapping = TextWrapping.Wrap });
            stackPanel.Children.Add(comboBox);

            var dialog = new ContentDialog
            {
                Title = Lang.Tr["Encoding"],
                Content = stackPanel,
                PrimaryButtonText = Lang.Tr["OK"],
                CloseButtonText = Lang.Tr["Cancel"],
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && comboBox.SelectedItem is EncodingInfo selected)
            {
                SelectedEncoding = selected;
            }
        }

        private async void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (_isClosing) return;

            if (_isDirty)
            {
                e.Cancel = true;
                
                var dialog = new ContentDialog
                {
                    Title = Lang.Tr["Prompt"],
                    Content = Lang.Tr["FileModifiedSavePrompt"],
                    PrimaryButtonText = Lang.Tr["Yes"],
                    SecondaryButtonText = Lang.Tr["No"],
                    CloseButtonText = Lang.Tr["Cancel"],
                    DefaultButton = ContentDialogButton.Primary
                };

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    _isClosing = true;
                    await SaveAndCloseAsync();
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    _isClosing = true;
                    _window.Close();
                }
            }
        }

        private async Task SaveAndCloseAsync()
        {
            if (await SaveFileAsync())
            {
                _isDirty = false;
                _window.Close();
            }
        }

        public async Task LoadFileAsync()
        {
            if (_fileSize > 5 * 1024 * 1024) // > 5MB
            {
                var dialog = new ContentDialog
                {
                    Title = Lang.Tr["LargeFileWarning"],
                    Content = string.Format(Lang.Tr["LargeFileWarningPrompt"], _fileSize / 1024 / 1024),
                    PrimaryButtonText = Lang.Tr["Yes"],
                    CloseButtonText = Lang.Tr["No"],
                    DefaultButton = ContentDialogButton.Primary
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    _window.Close();
                    return;
                }
            }

            await ReloadFileAsync();
        }

        private async Task ReloadFileAsync()
        {
            IsLoading = true;
            LoadingText = Lang.Tr["DownloadingFile"];
            StatusText = Lang.Tr["Loading"];

            try
            {
                await DoWithRetryAsync(async () =>
                {
                    var tempFile = Path.GetTempFileName();
                    try
                    {
                        var context = await _daemon.DownloadFileAsync(_realPath, tempFile, 1024 * 1024);

                        var progressTask = Task.Run(async () =>
                        {
                            while (!context.Done && context.State != NetworkLoadContextState.Closed)
                            {
                                if (_fileSize > 0)
                                {
                                    var progress = (double)context.LoadedBytes / _fileSize * 100;
                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        LoadingText = $"{Lang.Tr["DownloadingFile"]} {progress:F1}%";
                                    });
                                }
                                await Task.Delay(100);
                            }
                        });

                        if (context.NetworkLoadTask != null)
                        {
                            await context.NetworkLoadTask;
                        }
                        await progressTask;

                        LoadingText = Lang.Tr["ReadingFile"];

                        // Read file with selected encoding
                        using (var reader = new StreamReader(tempFile, _selectedEncoding.GetEncoding()))
                        {
                            var content = await reader.ReadToEndAsync();
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                Document.Text = content;
                                _isDirty = false;
                                StatusText = Lang.Tr["Ready"];
                            });
                        }
                    }
                    finally
                    {
                        try { File.Delete(tempFile); } catch { }
                    }
                }, TimeSpan.FromSeconds(1), 3);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[FileEditor] Failed to load file {0}", _realPath);
                
                var dialog = new ContentDialog
                {
                    Title = Lang.Tr["Error"],
                    Content = $"{Lang.Tr["LoadFileFailed"]}: {ex.Message}",
                    CloseButtonText = Lang.Tr["OK"]
                };
                await dialog.ShowAsync();
                
                StatusText = Lang.Tr["LoadFailed"];
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task DoWithRetryAsync(Func<Task> action, TimeSpan retryInterval, int maxAttemptCount = 3)
        {
            for (int i = 0; i < maxAttemptCount; i++)
            {
                try
                {
                    await action();
                    return;
                }
                catch (Exception ex)
                {
                    if (i == maxAttemptCount - 1)
                    {
                        throw;
                    }
                    Log.Warning(ex, "[FileEditor] Action failed, retrying in {0}ms...", retryInterval.TotalMilliseconds);
                    await Task.Delay(retryInterval);
                }
            }
        }

        private async Task<bool> SaveFileAsync()
        {
            IsLoading = true;
            LoadingText = Lang.Tr["SavingFile"];
            StatusText = Lang.Tr["Saving"];

            try
            {
                var tempFile = Path.GetTempFileName();
                
                // Write file with selected encoding
                using (var writer = new StreamWriter(tempFile, false, _selectedEncoding.GetEncoding()))
                {
                    await writer.WriteAsync(Document.Text);
                }

                var fileInfo = new FileInfo(tempFile);
                var uploadSize = fileInfo.Length;

                var context = await _daemon.UploadFileAsync(tempFile, _realPath, 1024 * 1024);
                
                var progressTask = Task.Run(async () =>
                {
                    while (!context.Done && context.State != NetworkLoadContextState.Closed)
                    {
                        if (uploadSize > 0)
                        {
                            var progress = (double)context.LoadedBytes / uploadSize * 100;
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                LoadingText = $"{Lang.Tr["SavingFile"]} {progress:F1}%";
                            });
                        }
                        await Task.Delay(100);
                    }
                });

                if (context.NetworkLoadTask != null)
                {
                    await context.NetworkLoadTask;
                }
                await progressTask;

                _isDirty = false;
                StatusText = Lang.Tr["Saved"];
                
                try { File.Delete(tempFile); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[FileEditor] Failed to save file {0}", _realPath);
                
                var dialog = new ContentDialog
                {
                    Title = Lang.Tr["Error"],
                    Content = $"{Lang.Tr["SaveFileFailed"]}: {ex.Message}",
                    CloseButtonText = Lang.Tr["OK"]
                };
                await dialog.ShowAsync();
                
                StatusText = Lang.Tr["SaveFailed"];
                return false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void FixHighlightingColors(IHighlightingDefinition definition)
        {
            if (definition == null) return;

            // Use a HashSet to track visited rule sets to prevent infinite recursion
            var visitedRuleSets = new System.Collections.Generic.HashSet<HighlightingRuleSet>();

            // Fix named colors
            foreach (var color in definition.NamedHighlightingColors)
            {
                FixColor(color);
            }

            // Fix main rule set
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

        private void RegisterCustomHighlighting()
        {
            RegisterHighlighting("Log", new[] { ".log", ".txt" }, "MCServerLauncher.WPF.Resources.SyntaxHighlighting.Log.xshd");
            RegisterHighlighting("Ini", new[] { ".ini", ".cfg", ".conf", ".properties" }, "MCServerLauncher.WPF.Resources.SyntaxHighlighting.Ini.xshd");
            RegisterHighlighting("Yaml", new[] { ".yaml", ".yml" }, "MCServerLauncher.WPF.Resources.SyntaxHighlighting.Yaml.xshd");
            RegisterHighlighting("Toml", new[] { ".toml" }, "MCServerLauncher.WPF.Resources.SyntaxHighlighting.Toml.xshd");
            RegisterHighlighting("Bat", new[] { ".bat", ".cmd" }, "MCServerLauncher.WPF.Resources.SyntaxHighlighting.Bat.xshd");
            RegisterHighlighting("Shell", new[] { ".sh", ".bash", ".zsh" }, "MCServerLauncher.WPF.Resources.SyntaxHighlighting.Shell.xshd");
            RegisterHighlighting("CSV", new[] { ".csv" }, "MCServerLauncher.WPF.Resources.SyntaxHighlighting.Csv.xshd");
        }

        private void RegisterHighlighting(string name, string[] extensions, string resourceName)
        {
            if (HighlightingManager.Instance.GetDefinition(name) != null) return;

            try
            {
                using (var stream = typeof(FileEditorWindow).Assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (var reader = new XmlTextReader(stream))
                        {
                            var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                            HighlightingManager.Instance.RegisterHighlighting(name, extensions, definition);
                        }
                    }
                    else
                    {
                        Log.Error("[FileEditor] Could not find embedded resource '{0}'. Available resources: {1}", 
                            resourceName, 
                            string.Join(", ", typeof(FileEditorWindow).Assembly.GetManifestResourceNames()));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[FileEditor] Failed to load syntax highlighting for {0}", name);
            }
        }
    }

    public class ThemeAwareHighlightingBrush : HighlightingBrush
    {
        private readonly Color _originalColor;
        private Brush _lightBrush;
        private Brush _darkBrush;

        public ThemeAwareHighlightingBrush(Color color)
        {
            _originalColor = color;
        }

        public override Brush GetBrush(ITextRunConstructionContext context)
        {
            var themeManager = iNKORE.UI.WPF.Modern.ThemeManager.Current;
            bool isDark = themeManager != null && themeManager.ActualApplicationTheme == iNKORE.UI.WPF.Modern.ApplicationTheme.Dark;

            if (isDark)
            {
                if (_originalColor == Colors.Black)
                {
                    return (Brush)Application.Current.TryFindResource("SystemControlPageTextBaseHighBrush");
                }

                if (_darkBrush == null)
                {
                    double factor = 0.5;
                    byte r = (byte)(_originalColor.R + (255 - _originalColor.R) * factor);
                    byte g = (byte)(_originalColor.G + (255 - _originalColor.G) * factor);
                    byte b = (byte)(_originalColor.B + (255 - _originalColor.B) * factor);
                    
                    var brush = new SolidColorBrush(Color.FromArgb(_originalColor.A, r, g, b));
                    brush.Freeze();
                    _darkBrush = brush;
                }
                return _darkBrush;
            }
            else
            {
                if (_lightBrush == null)
                {
                    var brush = new SolidColorBrush(_originalColor);
                    brush.Freeze();
                    _lightBrush = brush;
                }
                return _lightBrush;
            }
        }

        public override string ToString()
        {
            return _originalColor.ToString();
        }
    }
}
