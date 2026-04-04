using MCServerLauncher.Common.ProtoType.Serialization;

namespace MCServerLauncher.Common.Internal.Performance;

/// <summary>
/// Internal adapter for hot-path payload handling while preserving JsonPayloadBuffer null semantics.
/// This boundary is intentionally internal-only and netstandard2.1-safe.
/// </summary>
internal readonly struct JsonPayloadBufferView
{
    private readonly JsonPayloadBuffer _buffer;

    public JsonPayloadBufferView(JsonPayloadBuffer buffer)
    {
        _buffer = buffer;
    }

    public bool IsExplicitJsonNull => _buffer.IsExplicitJsonNull;

    public string RawJson => _buffer.GetRawText();

    public JsonPayloadBuffer Buffer => _buffer;

    public static bool TryCreate(JsonPayloadBuffer? buffer, out JsonPayloadBufferView view)
    {
        if (buffer is null)
        {
            view = default;
            return false;
        }

        view = new JsonPayloadBufferView(buffer.Value);
        return true;
    }
}
