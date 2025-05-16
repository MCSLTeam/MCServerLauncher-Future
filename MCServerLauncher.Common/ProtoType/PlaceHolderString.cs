using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Serilog;

namespace MCServerLauncher.Common.ProtoType;

/// <summary>
///     一个代表可替换环境变量的占位符得string(实际上就是string, 不过绑定了类型信息和占位符替换)
/// </summary>
public class PlaceHolderString
{
    private static readonly Regex KeyRegex = new(@"\{(\w+)\}", RegexOptions.Compiled);
    private readonly HashSet<string> _keys = new();

    public PlaceHolderString(string pattern)
    {
        Pattern = pattern;

        if (KeyRegex.IsMatch(pattern))
        {
            var matches = KeyRegex.Matches(pattern);
            foreach (Match match in matches) _keys.Add(match.Groups[1].Value);
        }
    }

    public string Pattern { get; }

    public bool TryApply<TMapping>(TMapping mapping, Func<string, TMapping, string?> supplier, out string? applied)
    {
        applied = Pattern;
        foreach (var key in _keys)
            try
            {
                var value = supplier.Invoke(key, mapping);
                if (value is null)
                {
                    applied = null;
                    return false;
                }

                applied = applied.Replace($"{{{key}}}", value);
            }
            catch (Exception e)
            {
                Log.Debug(e, "[PlaceHolderString] Could not apply pattern={0}: key={1} not found", applied, key);
                applied = null;
                return false;
            }

        return true;
    }

    public override string ToString()
    {
        return Pattern;
    }


    public class JsonConverter : JsonConverter<PlaceHolderString>
    {
        public override void WriteJson(JsonWriter writer, PlaceHolderString? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteValue(value.Pattern);
        }

        public override PlaceHolderString? ReadJson(JsonReader reader, Type objectType,
            PlaceHolderString? existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            var pattern = reader.Value?.ToString();

            return string.IsNullOrEmpty(pattern) ? null : new PlaceHolderString(pattern!);
        }
    }
}