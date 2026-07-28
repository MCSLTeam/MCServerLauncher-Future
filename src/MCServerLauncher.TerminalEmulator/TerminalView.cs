using System.Windows;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace MCServerLauncher.TerminalEmulator;

public readonly record struct TerminalSize(int Columns, int Rows);

public sealed class TerminalView : TextEditor
{
    private readonly TerminalBuffer _buffer = new(120, 40);
    private readonly TerminalColorizer _colorizer;
    private readonly TerminalCaretRenderer _caretRenderer;
    private readonly DispatcherTimer _caretTimer;
    private double _cellWidth = 8;
    private double _cellHeight = 16;
    private bool _usesExternalViewport;
    private double _viewportWidth;
    private double _viewportHeight;
    private string _documentText = string.Empty;
    private bool _caretVisible = true;

    public TerminalView()
    {
        _colorizer = new TerminalColorizer(_buffer);
        _caretRenderer = new TerminalCaretRenderer(this);
        FontFamily = new FontFamily("Cascadia Mono");
        FontSize = 13;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        IsReadOnly = true;
        WordWrap = false;
        ShowLineNumbers = false;
        HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled;
        Options.EnableTextDragDrop = false;
        Options.EnableRectangularSelection = true;
        TextArea.TextView.LineTransformers.Add(_colorizer);
        TextArea.TextView.BackgroundRenderers.Add(_caretRenderer);
        TextArea.Caret.CaretBrush = Brushes.Transparent;
        TextArea.Caret.Hide();
        InputMethod.SetIsInputMethodEnabled(this, true);
        InputMethod.SetIsInputMethodEnabled(TextArea, true);
        AddHandler(TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler(OnPreviewTextInput), true);
        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown), true);
        TextArea.PreviewMouseDown += (_, _) => TextArea.Focus();
        _caretTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(530), DispatcherPriority.Background, (_, _) => ToggleCaret(), Dispatcher);
        _caretTimer.Start();
    }

    public event EventHandler<string>? Input;
    public event EventHandler<TerminalSize>? TerminalSizeChanged;

    public TerminalBuffer Buffer => _buffer;
    public bool HasSelection => !string.IsNullOrEmpty(SelectedText);

    public void FocusInput()
    {
        MoveCaretToEnd();
        Focus();
        TextArea.Focus();
        Keyboard.Focus(TextArea);
    }

    public void UpdateViewport(double viewportWidth, double viewportHeight)
    {
        _usesExternalViewport = true;
        _viewportWidth = Math.Max(1, viewportWidth);
        _viewportHeight = Math.Max(1, viewportHeight);
        UpdateSizeFromViewport(_viewportWidth, _viewportHeight);
        UpdateDocumentExtent();
    }

    public void Feed(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return;
        _buffer.Feed(bytes);
        ReplaceDocumentText(_buffer.ViewText);
        MoveCaretToEnd();
        if (_usesExternalViewport)
            UpdateDocumentExtent();
        else
            UpdateSizeFromViewport();
    }

    public void Feed(ReadOnlyMemory<byte> bytes) => Feed(bytes.Span);

    public void Feed(IEnumerable<ReadOnlyMemory<byte>> chunks)
    {
        var hasData = false;
        foreach (var chunk in chunks)
        {
            if (chunk.IsEmpty)
                continue;
            _buffer.Feed(chunk.Span);
            hasData = true;
        }

        if (!hasData)
            return;

        ReplaceDocumentText(_buffer.ViewText);
        MoveCaretToEnd();
        if (_usesExternalViewport)
            UpdateDocumentExtent();
        else
            UpdateSizeFromViewport();
    }

    public void ClearTerminal()
    {
        _buffer.Clear();
        ReplaceDocumentText(_buffer.ViewText);
        MoveCaretToEnd();
        if (_usesExternalViewport)
            UpdateDocumentExtent();
    }

    public void BringEndIntoView() => ScrollToEnd();

    public void CopySelectionToClipboard()
    {
        if (HasSelection)
            Copy();
    }

    public void PasteFromClipboard()
    {
        if (Clipboard.ContainsText())
            EmitInput(Clipboard.GetText().Replace("\r\n", "\r"));
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_usesExternalViewport)
            UpdateDocumentExtent();
        else
            UpdateSizeFromViewport();
    }

    private void UpdateSizeFromViewport()
    {
        UpdateSizeFromViewport(ActualWidth, ActualHeight);
    }

    private void UpdateSizeFromViewport(double viewportWidth, double viewportHeight)
    {
        UpdateCellMetrics();
        var columns = Math.Clamp((int)(viewportWidth / _cellWidth), 20, 500);
        var rows = Math.Clamp((int)(viewportHeight / _cellHeight), 5, 200);
        if (columns == _buffer.Columns && rows == _buffer.Rows)
            return;
        _buffer.Resize(columns, rows);
        TerminalSizeChanged?.Invoke(this, new TerminalSize(columns, rows));
    }

    private void UpdateCellMetrics()
    {
        var sample = new FormattedText("M", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Brushes.Transparent,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        _cellWidth = Math.Max(1, Math.Ceiling(sample.WidthIncludingTrailingWhitespace));
        _cellHeight = Math.Max(1, Math.Ceiling(sample.Height));
    }

    private void UpdateDocumentExtent()
    {
        if (!_usesExternalViewport)
            return;

        var lineCount = Math.Max(1, Document?.LineCount ?? 1);
        var desiredHeight = Math.Max(_viewportHeight, lineCount * _cellHeight + 8);
        MinHeight = _viewportHeight;
        if (!double.IsNaN(desiredHeight) && !double.IsInfinity(desiredHeight) && Math.Abs(Height - desiredHeight) > 0.5)
            Height = desiredHeight;
    }

    private void ReplaceDocumentText(string text)
    {
        if (string.Equals(_documentText, text, StringComparison.Ordinal))
            return;

        _documentText = text;
        Document.Replace(0, Document.TextLength, text);
        TextArea.TextView.EnsureVisualLines();
        TextArea.TextView.Redraw();
        TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
            return;

        EmitInput(e.Text);
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Shift) != 0 && HasSelection)
            {
                CopySelectionToClipboard();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.V)
            {
                PasteFromClipboard();
                e.Handled = true;
                return;
            }
            var control = ToControlCharacter(e.Key);
            if (control is not null)
            {
                EmitInput(control);
                e.Handled = true;
                return;
            }
        }

        var sequence = e.Key switch
        {
            Key.Back => "\u007f",
            Key.Enter => "\r",
            Key.Tab => "\t",
            Key.Escape => "\u001b",
            Key.Up => _buffer.ApplicationCursorKeys ? "\u001bOA" : "\u001b[A",
            Key.Down => _buffer.ApplicationCursorKeys ? "\u001bOB" : "\u001b[B",
            Key.Right => _buffer.ApplicationCursorKeys ? "\u001bOC" : "\u001b[C",
            Key.Left => _buffer.ApplicationCursorKeys ? "\u001bOD" : "\u001b[D",
            Key.Home => "\u001b[H",
            Key.End => "\u001b[F",
            Key.Insert => "\u001b[2~",
            Key.Delete => "\u001b[3~",
            Key.PageUp => "\u001b[5~",
            Key.PageDown => "\u001b[6~",
            _ => null,
        };
        if (sequence is null)
            return;
        EmitInput(sequence);
        e.Handled = true;
    }

    private void EmitInput(string data)
    {
        if (!string.IsNullOrEmpty(data))
        {
            FocusInput();
            Input?.Invoke(this, data);
        }
    }

    private void MoveCaretToEnd()
    {
        if (TextArea.Caret.Offset != Document.TextLength)
            TextArea.Caret.Offset = Document.TextLength;
        TextArea.Caret.Hide();
        _caretVisible = true;
        TextArea.TextView.EnsureVisualLines();
        TextArea.TextView.Redraw();
        TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
    }

    private void ToggleCaret()
    {
        if (!IsKeyboardFocusWithin)
            return;

        _caretVisible = !_caretVisible;
        TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
    }

    private static string? ToControlCharacter(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
            return ((char)(key - Key.A + 1)).ToString();
        return key switch
        {
            Key.OemOpenBrackets => "\u001b",
            Key.OemBackslash => "\u001c",
            Key.OemCloseBrackets => "\u001d",
            Key.D6 => "\u001e",
            Key.OemMinus => "\u001f",
            _ => null,
        };
    }

    private sealed class TerminalColorizer(TerminalBuffer buffer) : DocumentColorizingTransformer
    {
        private readonly Dictionary<TerminalColor, SolidColorBrush> _brushes = [];

        protected override void ColorizeLine(DocumentLine line)
        {
            var row = line.LineNumber - 1;
            IReadOnlyList<TerminalCell> cells;
            if (row < buffer.ScrollbackLineCount)
                cells = buffer.GetScrollbackLine(row);
            else if (row - buffer.ScrollbackLineCount < buffer.Rows)
                cells = buffer.GetDisplayLine(row - buffer.ScrollbackLineCount);
            else
                return;

            for (var column = 0; column < cells.Count && line.Offset + column < line.EndOffset; column++)
            {
                var cell = cells[column];
                if (cell.Foreground is null && cell.Background is null && !cell.Bold && !cell.Underline)
                    continue;
                ChangeLinePart(line.Offset + column, line.Offset + column + 1, element =>
                {
                    if (cell.Foreground is { } foreground)
                        element.TextRunProperties.SetForegroundBrush(ToBrush(foreground));
                    if (cell.Background is { } background)
                        element.TextRunProperties.SetBackgroundBrush(ToBrush(background));
                    if (cell.Bold)
                        element.TextRunProperties.SetTypeface(new Typeface(
                            element.TextRunProperties.Typeface.FontFamily,
                            element.TextRunProperties.Typeface.Style,
                            FontWeights.Bold,
                            element.TextRunProperties.Typeface.Stretch));
                    if (cell.Underline)
                        element.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
                });
            }
        }

        private SolidColorBrush ToBrush(TerminalColor color)
        {
            if (_brushes.TryGetValue(color, out var cached))
                return cached;

            var brush = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));
            brush.Freeze();
            _brushes[color] = brush;
            return brush;
        }
    }

    private sealed class TerminalCaretRenderer(TerminalView owner) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Caret;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!owner._caretVisible || textView.Document is null || !owner.IsVisible)
                return;

            textView.EnsureVisualLines();
            var lineNumber = owner._buffer.ScrollbackLineCount + owner._buffer.CursorRow + 1;
            if (lineNumber < 1 || lineNumber > textView.Document.LineCount)
                return;

            var line = textView.Document.GetLineByNumber(lineNumber);
            var column = Math.Clamp(owner._buffer.CursorColumn, 0, line.Length);
            var position = new TextViewPosition(lineNumber, column + 1);
            var point = textView.GetVisualPosition(position, VisualYPosition.TextTop);
            var foreground = owner.Foreground as Brush ?? Brushes.White;
            drawingContext.DrawRectangle(foreground, null, new Rect(point.X, point.Y, 1.5, owner._cellHeight));
        }
    }
}