namespace MCServerLauncher.Daemon.Management.Extensions;

public static class IInstanceExtensions
{
    /// <summary>
    ///     尝试将实例转换为指定的实例类型
    /// </summary>
    /// <param name="instance"></param>
    /// <param name="castedInstance"></param>
    /// <typeparam name="TInstance"></typeparam>
    /// <returns></returns>
    public static bool TryCastTo<TInstance>(this IInstance instance, out TInstance? castedInstance)
        where TInstance : IInstance
    {
        var rv = instance.Config.CanCastTo<TInstance>();
        castedInstance = rv ? (TInstance)instance : default;
        return rv;
    }
}