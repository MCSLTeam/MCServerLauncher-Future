namespace MCServerLauncher.Common.Helpers;

public static class NullableExtensions
{
    public static TR? Map<T, TR>(this T? optional, Func<T, TR> map) where T : struct
    {
        return optional is null ? default : map(optional.Value);
    }
}