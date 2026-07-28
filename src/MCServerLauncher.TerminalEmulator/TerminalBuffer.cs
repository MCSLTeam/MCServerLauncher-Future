using System.Globalization;
using System.Text;
using System.Windows.Media;

namespace MCServerLauncher.TerminalEmulator;

public readonly record struct TerminalColor(byte Red, byte Green, byte Blue)
{
    public static TerminalColor FromRgb(byte red, byte green, byte blue) => new(red, green, blue);

    internal Color ToMediaColor() => Color.FromRgb(Red, Green, Blue);
}

public readonly record struct TerminalCell(
    char Character,
    TerminalColor? Foreground,
    TerminalColor? Background,
    bool Bold,
    bool Underline);

public sealed class TerminalBuffer
{
    private const int MaximumScrollbackLines = 1000;
    private static readonly TerminalColor[] AnsiColors =
    [
        TerminalColor.FromRgb(0x28, 0x2C, 0x34), TerminalColor.FromRgb(0xE0, 0x6C, 0x75),
        TerminalColor.FromRgb(0x98, 0xC3, 0x79), TerminalColor.FromRgb(0xE5, 0xC0, 0x7B),
        TerminalColor.FromRgb(0x61, 0xAF, 0xEF), TerminalColor.FromRgb(0xC6, 0x78, 0xDD),
        TerminalColor.FromRgb(0x56, 0xB6, 0xC2), TerminalColor.FromRgb(0xDC, 0xDF, 0xE4),
        TerminalColor.FromRgb(0x5C, 0x63, 0x70), TerminalColor.FromRgb(0xF0, 0x7B, 0x84),
        TerminalColor.FromRgb(0xB5, 0xD8, 0x8C), TerminalColor.FromRgb(0xF5, 0xD3, 0x91),
        TerminalColor.FromRgb(0x7C, 0xC5, 0xFF), TerminalColor.FromRgb(0xD9, 0x8B, 0xED),
        TerminalColor.FromRgb(0x6D, 0xD5, 0xDE), TerminalColor.FromRgb(0xFF, 0xFF, 0xFF),
    ];

    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private TerminalCell[,] _cells;
    private readonly List<TerminalCell[]> _scrollback = [];
    private int _row;
    private int _column;
    private int _savedRow;
    private int _savedColumn;
    private ParserState _state;
    private bool _oscEscapePending;
    private readonly StringBuilder _parameters = new();
    private TerminalColor? _foreground;
    private TerminalColor? _background;
    private bool _bold;
    private bool _underline;

    public TerminalBuffer(int columns, int rows)
    {
        ValidateSize(columns, rows);
        Columns = columns;
        Rows = rows;
        _cells = new TerminalCell[rows, columns];
    }

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public bool ApplicationCursorKeys { get; private set; }
    public int CursorRow => _row;
    public int CursorColumn => _column;
    public int ScrollbackLineCount => _scrollback.Count;

    public string Text => string.Join(Environment.NewLine, TrimTrailingEmptyLines(GetTextLines(includeScrollback: true)));

    public string ViewText => string.Join(Environment.NewLine, TrimTrailingEmptyLines(GetTextLines(includeScrollback: true, padCursorLine: true)));

    public TerminalCell GetCell(int row, int column)
    {
        if ((uint)row >= Rows || (uint)column >= Columns)
            throw new ArgumentOutOfRangeException();
        return _cells[row, column];
    }

    public IReadOnlyList<TerminalCell> GetDisplayLine(int displayRow)
    {
        if ((uint)displayRow >= Rows)
            throw new ArgumentOutOfRangeException(nameof(displayRow));
        return RowToArray(displayRow);
    }

    public IReadOnlyList<TerminalCell> GetScrollbackLine(int scrollbackRow)
    {
        if ((uint)scrollbackRow >= _scrollback.Count)
            throw new ArgumentOutOfRangeException(nameof(scrollbackRow));
        return _scrollback[scrollbackRow];
    }

    public void Feed(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return;

        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        _decoder.Convert(bytes, chars, flush: false, out _, out var used, out _);
        foreach (var character in chars.AsSpan(0, used))
            Process(character);
    }

    public void Resize(int columns, int rows)
    {
        ValidateSize(columns, rows);
        if (columns == Columns && rows == Rows)
            return;

        var resized = new TerminalCell[rows, columns];
        for (var row = 0; row < Math.Min(rows, Rows); row++)
        for (var column = 0; column < Math.Min(columns, Columns); column++)
            resized[row, column] = _cells[row, column];
        _cells = resized;
        Columns = columns;
        Rows = rows;
        _row = Math.Clamp(_row, 0, rows - 1);
        _column = Math.Clamp(_column, 0, columns - 1);
    }

    public void Clear()
    {
        _decoder.Reset();
        Array.Clear(_cells);
        _scrollback.Clear();
        _row = 0;
        _column = 0;
        _savedRow = 0;
        _savedColumn = 0;
        _state = ParserState.Normal;
        _oscEscapePending = false;
        _parameters.Clear();
        ResetGraphics();
        ApplicationCursorKeys = false;
    }

    private void Process(char character)
    {
        if (_state == ParserState.Escape)
        {
            ProcessEscape(character);
            return;
        }
        if (_state is ParserState.Csi or ParserState.Osc)
        {
            ProcessSequence(character);
            return;
        }

        switch (character)
        {
            case '\u001b': _state = ParserState.Escape; break;
            case '\r': _column = 0; break;
            case '\n':
                _column = 0;
                MoveDown();
                break;
            case '\b': _column = Math.Max(0, _column - 1); break;
            case '\t': _column = Math.Min(Columns - 1, ((_column / 8) + 1) * 8); break;
            default:
                if (!char.IsControl(character))
                    Put(character);
                break;
        }
    }

    private void ProcessEscape(char character)
    {
        _state = ParserState.Normal;
        switch (character)
        {
            case '[':
                _parameters.Clear();
                _state = ParserState.Csi;
                break;
            case ']':
                _state = ParserState.Osc;
                break;
            case '7': SaveCursor(); break;
            case '8': RestoreCursor(); break;
            case 'c': Clear(); break;
        }
    }

    private void ProcessSequence(char character)
    {
        if (_state == ParserState.Osc)
        {
            if (_oscEscapePending)
            {
                _oscEscapePending = false;
                if (character == '[')
                {
                    _parameters.Clear();
                    _state = ParserState.Csi;
                    return;
                }
                if (character == '\\')
                {
                    _state = ParserState.Normal;
                    return;
                }

                _state = ParserState.Normal;
                Process(character);
                return;
            }

            if (character == '\a')
                _state = ParserState.Normal;
            else if (character == '\u001b')
                _oscEscapePending = true;
            return;
        }

        if (character is >= '@' and <= '~')
        {
            ExecuteCsi(character, _parameters.ToString());
            _parameters.Clear();
            _state = ParserState.Normal;
            return;
        }
        if (_parameters.Length < 256)
            _parameters.Append(character);
        else
            _state = ParserState.Normal;
    }

    private void ExecuteCsi(char command, string text)
    {
        var privateMode = text.StartsWith('?');
        var parameters = ParseParameters(text.TrimStart('?', '>', '!'));
        var first = ValueOrDefault(parameters, 0, 1);
        switch (command)
        {
            case 'A': _row = Math.Max(0, _row - first); break;
            case 'B': _row = Math.Min(Rows - 1, _row + first); break;
            case 'C': _column = Math.Min(Columns - 1, _column + first); break;
            case 'D': _column = Math.Max(0, _column - first); break;
            case 'G': _column = Math.Clamp(first - 1, 0, Columns - 1); break;
            case 'd': _row = Math.Clamp(first - 1, 0, Rows - 1); break;
            case 'H' or 'f':
                _row = Math.Clamp(ValueOrDefault(parameters, 0, 1) - 1, 0, Rows - 1);
                _column = Math.Clamp(ValueOrDefault(parameters, 1, 1) - 1, 0, Columns - 1);
                break;
            case 'J': EraseDisplay(ValueOrDefault(parameters, 0, 0)); break;
            case 'K': EraseLine(ValueOrDefault(parameters, 0, 0)); break;
            case 'm': ApplyGraphics(parameters); break;
            case 's': SaveCursor(); break;
            case 'u': RestoreCursor(); break;
            case 'h' when privateMode && parameters.Contains(1): ApplicationCursorKeys = true; break;
            case 'l' when privateMode && parameters.Contains(1): ApplicationCursorKeys = false; break;
        }
    }

    private void ApplyGraphics(int[] parameters)
    {
        if (parameters.Length == 0)
        {
            ResetGraphics();
            return;
        }

        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            switch (parameter)
            {
                case 0: ResetGraphics(); break;
                case 1: _bold = true; break;
                case 4: _underline = true; break;
                case 22: _bold = false; break;
                case 24: _underline = false; break;
                case 39: _foreground = null; break;
                case 49: _background = null; break;
                case >= 30 and <= 37: _foreground = AnsiColors[parameter - 30]; break;
                case >= 40 and <= 47: _background = AnsiColors[parameter - 40]; break;
                case >= 90 and <= 97: _foreground = AnsiColors[parameter - 90 + 8]; break;
                case >= 100 and <= 107: _background = AnsiColors[parameter - 100 + 8]; break;
                case 38 or 48 when index + 1 < parameters.Length:
                    var foreground = parameter == 38;
                    var mode = parameters[++index];
                    if (mode == 5 && index + 1 < parameters.Length)
                    {
                        SetColor(foreground, IndexedColor(parameters[++index]));
                    }
                    else if (mode == 2 && index + 3 < parameters.Length)
                    {
                        SetColor(foreground, TerminalColor.FromRgb(
                            (byte)Math.Clamp(parameters[++index], 0, 255),
                            (byte)Math.Clamp(parameters[++index], 0, 255),
                            (byte)Math.Clamp(parameters[++index], 0, 255)));
                    }
                    break;
            }
        }
    }

    private static TerminalColor IndexedColor(int index)
    {
        index = Math.Clamp(index, 0, 255);
        if (index < 16)
            return AnsiColors[index];
        if (index is >= 232)
        {
            var shade = (byte)(8 + (index - 232) * 10);
            return TerminalColor.FromRgb(shade, shade, shade);
        }
        index -= 16;
        return TerminalColor.FromRgb(
            (byte)(index / 36 * 51),
            (byte)(index % 36 / 6 * 51),
            (byte)(index % 6 * 51));
    }

    private void SetColor(bool foreground, TerminalColor color)
    {
        if (foreground)
            _foreground = color;
        else
            _background = color;
    }

    private void ResetGraphics()
    {
        _foreground = null;
        _background = null;
        _bold = false;
        _underline = false;
    }

    private void Put(char character)
    {
        if (_column >= Columns)
        {
            _column = 0;
            MoveDown();
        }
        _cells[_row, _column] = new TerminalCell(character, _foreground, _background, _bold, _underline);
        _column++;
    }

    private void MoveDown()
    {
        if (_row < Rows - 1)
        {
            _row++;
            return;
        }
        AddScrollback(RowToArray(0));
        for (var row = 1; row < Rows; row++)
        for (var column = 0; column < Columns; column++)
            _cells[row - 1, column] = _cells[row, column];
        for (var column = 0; column < Columns; column++)
            _cells[Rows - 1, column] = default;
    }

    private void EraseDisplay(int mode)
    {
        if (mode is 2 or 3)
        {
            ClearDisplay();
            return;
        }
        if (mode == 0)
        {
            EraseLine(0);
            for (var row = _row + 1; row < Rows; row++)
                ClearRow(row);
        }
        else if (mode == 1)
        {
            EraseLine(1);
            for (var row = 0; row < _row; row++)
                ClearRow(row);
        }
    }

    private void EraseLine(int mode)
    {
        var start = mode == 1 ? 0 : _column;
        var end = mode == 0 ? Columns - 1 : _column;
        if (mode == 2)
            (start, end) = (0, Columns - 1);
        for (var column = start; column <= end; column++)
            _cells[_row, column] = default;
    }

    private void ClearDisplay()
    {
        Array.Clear(_cells);
        _row = 0;
        _column = 0;
    }

    private void AddScrollback(TerminalCell[] line)
    {
        _scrollback.Add(line);
        if (_scrollback.Count > MaximumScrollbackLines)
            _scrollback.RemoveAt(0);
    }

    private TerminalCell[] RowToArray(int row)
    {
        var line = new TerminalCell[Columns];
        for (var column = 0; column < Columns; column++)
            line[column] = _cells[row, column];
        return line;
    }

    private IEnumerable<string> GetTextLines(bool includeScrollback, bool padCursorLine = false)
    {
        if (includeScrollback)
        {
            foreach (var line in _scrollback)
                yield return CellsToString(line);
        }
        for (var row = 0; row < Rows; row++)
            yield return CellsToString(RowToArray(row), padCursorLine && row == _row && _column > 0 ? _column + 1 : 0);
    }

    public static string CellsToString(IReadOnlyList<TerminalCell> cells, int minimumLength = 0)
    {
        var end = cells.Count;
        while (end > 0 && cells[end - 1].Character == '\0')
            end--;
        end = Math.Max(end, Math.Clamp(minimumLength, 0, cells.Count));
        if (end == 0)
            return string.Empty;
        var chars = new char[end];
        for (var index = 0; index < end; index++)
            chars[index] = cells[index].Character == '\0' ? ' ' : cells[index].Character;
        return new string(chars);
    }

    private void ClearRow(int row)
    {
        for (var column = 0; column < Columns; column++)
            _cells[row, column] = default;
    }

    private void SaveCursor() => (_savedRow, _savedColumn) = (_row, _column);

    private void RestoreCursor()
    {
        _row = Math.Clamp(_savedRow, 0, Rows - 1);
        _column = Math.Clamp(_savedColumn, 0, Columns - 1);
    }

    private static int[] ParseParameters(string text) => text.Length == 0
        ? []
        : text.Split(';').Select(value => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0).ToArray();

    private static int ValueOrDefault(int[] values, int index, int fallback) =>
        index < values.Length && values[index] > 0 ? values[index] : fallback;

    private static void ValidateSize(int columns, int rows)
    {
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
    }

    private static IReadOnlyList<string> TrimTrailingEmptyLines(IEnumerable<string> lines)
    {
        var list = lines.ToList();
        while (list.Count > 0 && list[^1].Length == 0)
            list.RemoveAt(list.Count - 1);
        return list;
    }

    private enum ParserState { Normal, Escape, Csi, Osc }
}