using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.InstanceConsole.View.Dialogs;
using MCServerLauncher.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Threading;
using MCServerLauncher.TerminalEmulator;
using System.Xml;

namespace MCServerLauncher.WPF.InstanceConsole.View.Pages
{
    public partial class CommandPage
    {
        private static bool isFullscreen = false;
        private bool _isPageLoaded = false;
        private readonly CommandPageViewModel _viewModel;
        private readonly SemaphoreSlim _consoleGate = new(1, 1);
        private DaemonConsoleSession? _consoleSession;
        private CancellationTokenSource? _consoleCancellation;
        private Task? _consolePump;
        private ushort _consoleColumns;
        private ushort _consoleRows;
        private bool _isDisposed;
        private readonly object _pipeHistoryGate = new();
        private bool _isLoadingPipeHistory;
        private readonly List<string> _pendingPipeLogs = [];
        private readonly List<string> _queuedPipeLogs = [];
        private bool _isPipeLogFlushScheduled;
        private readonly object _ptyOutputGate = new();
        private readonly Queue<ReadOnlyMemory<byte>> _queuedPtyOutput = new();
        private bool _isPtyOutputFlushScheduled;
        private const int MaximumPtyOutputChunksPerFlush = 64;

        public CommandPage()
        {
            InitializeComponent();
            _viewModel = App.Services.GetRequiredService<CommandPageViewModel>();
            DataContext = _viewModel;
            OnFullscreenButtonContent.Visibility = Visibility.Visible;
            OffFullscreenButtonContent.Visibility = Visibility.Collapsed;
            ConsoleLogEditor.PreviewMouseWheel += (_, e) => ForwardMouseWheel(PipeLogScrollViewer, e);
            PtyTerminal.PreviewMouseWheel += (_, e) => ForwardMouseWheel(PtyTerminalScrollViewer, e);
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
                InstanceDataManager.Instance.ReportUpdated += OnReportUpdated;
                iNKORE.UI.WPF.Modern.ThemeManager.Current.ActualApplicationThemeChanged += OnApplicationThemeChanged;
                ConfigurePtyTerminalTheme();
                ApplyConsoleMode(IsPtyConfigured() ? ConsoleMode.Pty : ConsoleMode.Pipe);
                if (IsPtyConfigured())
                {
                    await EnsurePtySessionAsync();
                }
                else
                {
                    InstanceDataManager.Instance.LogReceived += OnLogReceived;
                    CommandInputTextBox.Focus();
                    await LoadLogHistoryAsync();
                }
            }
        }

        public async Task DisposeAsync()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            InstanceDataManager.Instance.LogReceived -= OnLogReceived;
            InstanceDataManager.Instance.ReportUpdated -= OnReportUpdated;
            iNKORE.UI.WPF.Modern.ThemeManager.Current.ActualApplicationThemeChanged -= OnApplicationThemeChanged;
            await ClosePtySessionAsync();
            _viewModel.Dispose();
            _isPageLoaded = false;
            _consoleGate.Dispose();
        }

        private void OnLogReceived(object? sender, string logMessage)
        {
            var shouldScheduleFlush = false;
            lock (_pipeHistoryGate)
            {
                if (_isLoadingPipeHistory)
                {
                    _pendingPipeLogs.Add(logMessage);
                    return;
                }

                _queuedPipeLogs.Add(logMessage);
                if (!_isPipeLogFlushScheduled)
                {
                    _isPipeLogFlushScheduled = true;
                    shouldScheduleFlush = true;
                }
            }

            if (shouldScheduleFlush)
                _ = Dispatcher.BeginInvoke(FlushPipeLogQueue, DispatcherPriority.Background);
        }

        private void FlushPipeLogQueue()
        {
            List<string> logs;
            lock (_pipeHistoryGate)
            {
                logs = [.. _queuedPipeLogs];
                _queuedPipeLogs.Clear();
                _isPipeLogFlushScheduled = false;
            }

            if (logs.Count == 0)
                return;

            var wasAtEnd = IsAtBottom(PipeLogScrollViewer);
            ConsoleLogEditor.AppendText(string.Join(Environment.NewLine, logs) + Environment.NewLine);
            UpdatePipeLogExtent();
            if (wasAtEnd)
                PipeLogScrollViewer.ScrollToEnd();
        }

        private async void CommandInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await _viewModel.SendCommandCommand.ExecuteAsync(null);
            }
        }

        private bool IsPtyConfigured() =>
            InstanceDataManager.Instance.CurrentReport?.Config.ConsoleMode == ConsoleMode.Pty;

        internal static bool ShouldShowCommandInput(ConsoleMode consoleMode) =>
            consoleMode != ConsoleMode.Pty;

        private void ApplyConsoleMode(ConsoleMode consoleMode)
        {
            CommandInputPanel.Visibility = ShouldShowCommandInput(consoleMode)
                ? Visibility.Visible
                : Visibility.Collapsed;
            ConsoleLogEditor.Visibility = consoleMode == ConsoleMode.Pty
                ? Visibility.Collapsed
                : Visibility.Visible;
            PipeLogScrollViewer.Visibility = consoleMode == ConsoleMode.Pty
                ? Visibility.Collapsed
                : Visibility.Visible;
            PtyTerminalScrollViewer.Visibility = consoleMode == ConsoleMode.Pty
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdatePipeLogExtent();
            UpdatePtyTerminalViewport();
        }

        private void ConfigurePtyTerminalTheme()
        {
            var isDark = iNKORE.UI.WPF.Modern.ThemeManager.Current?.ActualApplicationTheme ==
                iNKORE.UI.WPF.Modern.ApplicationTheme.Dark;
            var background = isDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xFF, 0xFF, 0xFF);
            var foreground = isDark ? Color.FromRgb(0xF1, 0xF1, 0xF1) : Color.FromRgb(0x20, 0x20, 0x20);
            PtyTerminal.Foreground = new SolidColorBrush(foreground);
        }

        private void OnApplicationThemeChanged(iNKORE.UI.WPF.Modern.ThemeManager sender, object e) =>
            Dispatcher.Invoke(ConfigurePtyTerminalTheme);

        private async Task LoadLogHistoryAsync()
        {
            lock (_pipeHistoryGate)
                _isLoadingPipeHistory = true;
            try
            {
                var history = await InstanceDataManager.Instance.GetInstanceLogHistoryAsync();
                await Dispatcher.InvokeAsync(() =>
                {
                    List<string> pending;
                    lock (_pipeHistoryGate)
                    {
                        pending = [.. _pendingPipeLogs];
                        _pendingPipeLogs.Clear();
                        _isLoadingPipeHistory = false;
                    }

                    ConsoleLogEditor.Clear();
                    if (history is { Length: > 0 })
                        ConsoleLogEditor.AppendText(string.Join(Environment.NewLine, history) + Environment.NewLine);
                    foreach (var line in MergePipeHistory(history ?? [], pending).Skip(history?.Length ?? 0))
                        ConsoleLogEditor.AppendText(line + Environment.NewLine);
                    UpdatePipeLogExtent();
                    PipeLogScrollViewer.ScrollToEnd();
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CommandPage] Failed to load log history");
                await Dispatcher.InvokeAsync(() =>
                {
                    List<string> pending;
                    lock (_pipeHistoryGate)
                    {
                        pending = [.. _pendingPipeLogs];
                        _pendingPipeLogs.Clear();
                        _isLoadingPipeHistory = false;
                    }

                    ConsoleLogEditor.AppendText($"[ERROR] Failed to load log history: {ex.Message}{Environment.NewLine}");
                    foreach (var line in pending)
                        ConsoleLogEditor.AppendText(line + Environment.NewLine);
                    UpdatePipeLogExtent();
                    PipeLogScrollViewer.ScrollToEnd();
                });
            }
            finally
            {
                lock (_pipeHistoryGate)
                    _isLoadingPipeHistory = false;
            }
        }

        internal static IReadOnlyList<string> MergePipeHistory(
            IReadOnlyList<string> history,
            IReadOnlyList<string> pending)
        {
            var overlap = Math.Min(history.Count, pending.Count);
            while (overlap > 0)
            {
                var matches = true;
                for (var index = 0; index < overlap; index++)
                {
                    if (!string.Equals(history[history.Count - overlap + index], pending[index], StringComparison.Ordinal))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                    break;
                overlap--;
            }

            return [.. history, .. pending.Skip(overlap)];
        }

        private void OnReportUpdated(object? sender, InstanceReport? report)
        {
            if (!_isPageLoaded || _isDisposed || report is null)
                return;

            ApplyConsoleMode(report.Config.ConsoleMode);
            if (report.Config.ConsoleMode == ConsoleMode.Pty && IsProcessUp(report.Status))
            {
                _ = EnsurePtySessionAsync();
            }
            else
            {
                _ = ClosePtySessionAsync();
                if (report.Config.ConsoleMode != ConsoleMode.Pty)
                    RestorePipeConsole();
            }
        }

        private void RestorePipeConsole()
        {
            ApplyConsoleMode(ConsoleMode.Pipe);
            ConsoleLogEditor.TextArea.Caret.Hide();
            InstanceDataManager.Instance.LogReceived -= OnLogReceived;
            InstanceDataManager.Instance.LogReceived += OnLogReceived;
            _ = LoadLogHistoryAsync();
        }

        private async Task EnsurePtySessionAsync()
        {
            if (!IsPtyConfigured() ||
                InstanceDataManager.Instance.CurrentReport is not { } report ||
                !IsProcessUp(report.Status))
            {
                return;
            }

            ApplyConsoleMode(ConsoleMode.Pty);

            await _consoleGate.WaitAsync();
            try
            {
                if (_consoleSession is not null)
                    return;

                ClearQueuedPtyOutput();
                PtyTerminal.ClearTerminal();
                var (columns, rows) = GetTerminalSize();
                var session = await InstanceDataManager.Instance.OpenConsoleAsync(columns, rows);
                _consoleSession = session;
                _consoleColumns = columns;
                _consoleRows = rows;
                _consoleCancellation = new CancellationTokenSource();
                InstanceDataManager.Instance.LogReceived -= OnLogReceived;
                UpdatePtyTerminalViewport();
                PtyTerminal.FocusInput();
                _consolePump = PumpPtyOutputAsync(session, _consoleCancellation.Token);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CommandPage] Failed to open PTY console session");
            }
            finally
            {
                _consoleGate.Release();
            }
        }

        private async Task ClosePtySessionAsync()
        {
            DaemonConsoleSession? session;
            CancellationTokenSource? cancellation;
            Task? pump;
            await _consoleGate.WaitAsync();
            try
            {
                session = _consoleSession;
                cancellation = _consoleCancellation;
                pump = _consolePump;
                _consoleSession = null;
                _consoleCancellation = null;
                _consolePump = null;
                _consoleColumns = 0;
                _consoleRows = 0;
            }
            finally
            {
                _consoleGate.Release();
            }

            cancellation?.Cancel();
            if (pump is not null)
            {
                try { await pump; }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Log.Debug(ex, "[CommandPage] PTY output pump stopped"); }
            }
            cancellation?.Dispose();
            if (session is not null)
                await session.DisposeAsync();
            await Dispatcher.InvokeAsync(() =>
            {
                if (_consoleSession is not null)
                    return;
                ClearQueuedPtyOutput();
                PtyTerminal.ClearTerminal();
            }, DispatcherPriority.Background);
        }

        private async Task PumpPtyOutputAsync(DaemonConsoleSession session, CancellationToken cancellationToken)
        {
            try
            {
                while (await session.Output.WaitToReadAsync(cancellationToken))
                {
                    if (!session.Output.TryRead(out var chunk))
                        continue;
                    QueuePtyOutput(session, chunk.Data);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CommandPage] PTY console output failed");
            }
            finally
            {
                await DetachClosedPtySessionAsync(session);
            }
        }

        private async Task DetachClosedPtySessionAsync(DaemonConsoleSession session)
        {
            CancellationTokenSource? cancellation = null;
            await _consoleGate.WaitAsync();
            try
            {
                if (!ReferenceEquals(session, _consoleSession))
                    return;

                _consoleSession = null;
                cancellation = _consoleCancellation;
                _consoleCancellation = null;
                _consolePump = null;
                _consoleColumns = 0;
                _consoleRows = 0;
            }
            finally
            {
                _consoleGate.Release();
            }

            cancellation?.Dispose();
            await session.DisposeAsync();
        }

        private void QueuePtyOutput(DaemonConsoleSession session, ReadOnlyMemory<byte> data)
        {
            if (data.IsEmpty)
                return;

            var shouldSchedule = false;
            lock (_ptyOutputGate)
            {
                if (!ReferenceEquals(session, _consoleSession))
                    return;

                _queuedPtyOutput.Enqueue(data);
                if (!_isPtyOutputFlushScheduled)
                {
                    _isPtyOutputFlushScheduled = true;
                    shouldSchedule = true;
                }
            }

            if (shouldSchedule)
                _ = Dispatcher.BeginInvoke(() => FlushQueuedPtyOutput(session), DispatcherPriority.Render);
        }

        private void FlushQueuedPtyOutput(DaemonConsoleSession session)
        {
            var chunks = new List<ReadOnlyMemory<byte>>(MaximumPtyOutputChunksPerFlush);
            var shouldReschedule = false;
            lock (_ptyOutputGate)
            {
                if (!ReferenceEquals(session, _consoleSession))
                    return;

                while (chunks.Count < MaximumPtyOutputChunksPerFlush && _queuedPtyOutput.TryDequeue(out var chunk))
                    chunks.Add(chunk);

                shouldReschedule = _queuedPtyOutput.Count > 0;
                _isPtyOutputFlushScheduled = shouldReschedule;
            }

            if (chunks.Count > 0)
            {
                var wasAtEnd = IsAtBottom(PtyTerminalScrollViewer);
                PtyTerminal.Feed(chunks);
                UpdatePtyTerminalViewport();
                if (wasAtEnd)
                    PtyTerminalScrollViewer.ScrollToEnd();
            }

            if (shouldReschedule)
                _ = Dispatcher.BeginInvoke(() => FlushQueuedPtyOutput(session), DispatcherPriority.Render);
        }

        private void ClearQueuedPtyOutput()
        {
            lock (_ptyOutputGate)
            {
                _queuedPtyOutput.Clear();
                _isPtyOutputFlushScheduled = false;
            }
        }

        private async Task SendPtyInputAsync(ReadOnlyMemory<byte> data)
        {
            var session = _consoleSession;
            if (session is null || data.IsEmpty)
                return;

            var result = await session.WriteAsync(data);
            if (result.IsErr(out var error))
                Log.Warning("[CommandPage] PTY input rejected: {Code}: {Message}", error!.Code, error.Message);
        }

        private void PipeLogScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePipeLogExtent();

        private void PtyTerminalScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePtyTerminalViewport();

        private void UpdatePipeLogExtent()
        {
            if (PipeLogScrollViewer.ActualHeight <= 0)
                return;

            var lineHeight = MeasureLineHeight(ConsoleLogEditor.FontFamily, ConsoleLogEditor.FontStyle,
                ConsoleLogEditor.FontWeight, ConsoleLogEditor.FontStretch, ConsoleLogEditor.FontSize);
            var lineCount = Math.Max(1, ConsoleLogEditor.Document?.LineCount ?? 1);
            ConsoleLogEditor.TextArea.TextView.EnsureVisualLines();
            var documentHeight = Math.Max(ConsoleLogEditor.TextArea.TextView.DocumentHeight, lineCount * lineHeight);
            var desiredHeight = Math.Max(PipeLogScrollViewer.ActualHeight, documentHeight + 8);
            ConsoleLogEditor.MinHeight = PipeLogScrollViewer.ActualHeight;
            if (Math.Abs(ConsoleLogEditor.Height - desiredHeight) > 0.5)
                ConsoleLogEditor.Height = desiredHeight;
        }

        private void UpdatePtyTerminalViewport()
        {
            if (PtyTerminalScrollViewer.ActualWidth <= 0 || PtyTerminalScrollViewer.ActualHeight <= 0)
                return;

            PtyTerminal.UpdateViewport(PtyTerminalScrollViewer.ActualWidth, PtyTerminalScrollViewer.ActualHeight);
        }

        private double MeasureLineHeight(FontFamily fontFamily, FontStyle fontStyle, FontWeight fontWeight,
            FontStretch fontStretch, double fontSize)
        {
            var sample = new FormattedText("M", System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, new Typeface(fontFamily, fontStyle, fontWeight, fontStretch), fontSize,
                Brushes.Transparent, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            return Math.Max(1, Math.Ceiling(sample.Height));
        }

        private static bool IsAtBottom(ScrollViewer scrollViewer) =>
            scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 2;

        private static void ForwardMouseWheel(ScrollViewer scrollViewer, MouseWheelEventArgs e)
        {
            e.Handled = true;
            scrollViewer.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = e.Source
            });
        }

        private async Task ResizePtyAsync(ushort columns, ushort rows)
        {
            var session = _consoleSession;
            if (session is null)
                return;

            if (columns == _consoleColumns && rows == _consoleRows)
                return;

            var result = await session.ResizeAsync(columns, rows);
            if (result.IsErr(out var error))
            {
                Log.Warning("[CommandPage] PTY resize rejected: {Code}: {Message}", error!.Code, error.Message);
                return;
            }

            _consoleColumns = columns;
            _consoleRows = rows;
        }

        private (ushort Columns, ushort Rows) GetTerminalSize()
        {
            var columns = Math.Clamp(PtyTerminal.Buffer.Columns, 20, 500);
            var rows = Math.Clamp(PtyTerminal.Buffer.Rows, 5, 200);
            return ((ushort)columns, (ushort)rows);
        }

        private static bool IsProcessUp(InstanceStatus status) =>
            status is InstanceStatus.Running or InstanceStatus.Starting;

        private void PtyTerminal_Input(object? sender, string data) =>
            _ = SendPtyInputAsync(Encoding.UTF8.GetBytes(data));

        private void PtyTerminal_TerminalSizeChanged(object? sender, TerminalSize size) =>
            _ = ResizePtyAsync((ushort)Math.Clamp(size.Columns, 20, 500), (ushort)Math.Clamp(size.Rows, 5, 200));

        private void PtyTerminal_CopyClick(object sender, RoutedEventArgs e) => PtyTerminal.CopySelectionToClipboard();

        private void PtyTerminal_PasteClick(object sender, RoutedEventArgs e) => PtyTerminal.PasteFromClipboard();

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
