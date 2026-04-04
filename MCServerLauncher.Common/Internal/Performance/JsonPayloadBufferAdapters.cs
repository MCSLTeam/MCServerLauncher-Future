using System;
using MCServerLauncher.Common.ProtoType.Serialization;

namespace MCServerLauncher.Common.Internal.Performance;

/// <summary>
/// Internal synchronous adapters around JsonPayloadBuffer for low-allocation hot-path use.
/// </summary>
internal static class JsonPayloadBufferAdapters
{
    public static string GetRawJson(in JsonPayloadBufferView view)
    {
        return view.RawJson;
    }

    public static bool TryGetNonNullRawJson(JsonPayloadBuffer? buffer, out string? rawJson)
    {
        if (!JsonPayloadBufferView.TryCreate(buffer, out var view))
        {
            rawJson = null;
            return false;
        }

        if (view.IsExplicitJsonNull)
        {
            rawJson = null;
            return false;
        }

        rawJson = view.RawJson;
        return true;
    }

    public static bool TryCreateView(JsonPayloadBuffer? buffer, out JsonPayloadBufferView view)
    {
        return JsonPayloadBufferView.TryCreate(buffer, out view);
    }

    public static TResult Map<TResult>(in JsonPayloadBufferView view, Func<JsonPayloadBuffer, TResult> mapper)
    {
        return mapper(view.Buffer);
    }
}
