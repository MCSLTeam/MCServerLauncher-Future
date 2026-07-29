using System.Text;

namespace MCServerLauncher.Daemon.Management.Communicate;

internal sealed class PtyLogDecoder
{
    private readonly Decoder _decoder;
    private readonly StringBuilder _line = new();
    private readonly StringBuilder _escape = new();
    private EscapeState _escapeState;

    internal PtyLogDecoder(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        _decoder = encoding.GetDecoder();
    }

    internal IReadOnlyList<string> Append(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return [];

        var chars = new char[_decoder.GetCharCount(bytes, flush: false)];
        _decoder.Convert(bytes, chars, flush: false, out _, out var charsUsed, out _);
        return Process(chars.AsSpan(0, charsUsed));
    }

    internal IReadOnlyList<string> Complete()
    {
        var chars = new char[8];
        _decoder.Convert([], chars, flush: true, out _, out var charsUsed, out _);
        var lines = Process(chars.AsSpan(0, charsUsed)).ToList();
        _escape.Clear();
        _escapeState = EscapeState.None;
        if (_line.Length > 0)
        {
            var line = _line.ToString();
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
            _line.Clear();
        }

        return lines;
    }

    private IReadOnlyList<string> Process(ReadOnlySpan<char> chars)
    {
        List<string>? lines = null;
        foreach (var character in chars)
        {
            if (_escapeState != EscapeState.None)
            {
                if (_escapeState == EscapeState.Introducer)
                {
                    _escapeState = character switch
                    {
                        '[' => EscapeState.Csi,
                        ']' => EscapeState.Osc,
                        _ => EscapeState.None
                    };
                    continue;
                }

                _escape.Append(character);
                if ((_escapeState == EscapeState.Csi && character is >= '@' and <= '~') ||
                    (_escapeState == EscapeState.Osc && character == '\u0007'))
                {
                    _escape.Clear();
                    _escapeState = EscapeState.None;
                }
                else if (_escape.Length > 128)
                {
                    _escape.Clear();
                    _escapeState = EscapeState.None;
                }

                continue;
            }

            if (character == '\u001b')
            {
                _escapeState = EscapeState.Introducer;
                _escape.Clear();
                continue;
            }

            if (character == '\n')
            {
                var line = _line.ToString().TrimEnd('\r');
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines ??= [];
                    lines.Add(line);
                }
                _line.Clear();
                continue;
            }

            _line.Append(character);
        }

        return lines ?? [];
    }

    private enum EscapeState
    {
        None,
        Introducer,
        Csi,
        Osc
    }
}
