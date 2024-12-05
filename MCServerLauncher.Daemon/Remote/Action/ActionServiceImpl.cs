using System.Reflection;
using MCServerLauncher.Daemon.Minecraft.Server;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils.Cache;
using Newtonsoft.Json.Linq;
using Exception = System.Exception;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     Action(rpc)处理器。
///     Action是ws交互格式的一种，他代表的是一个远程过程调用: C->S,处理后返回json数据给C
/// </summary>
internal class ActionServiceImpl : IActionService
{
    private readonly ActionHandler _actionHandler;

    private static string ToSnakeCase(string str)
        => string.Concat(str.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString())).ToLower();

    public ActionServiceImpl(IAsyncTimedCacheable<List<JavaScanner.JavaInfo>> javaScannerCache,
        IInstanceManager instanceManager, IEventService eventService)
    {
        _actionHandler = new ActionHandler(javaScannerCache, instanceManager, eventService);
    }

    /// <summary>
    ///     Action(rpc)处理中枢
    /// </summary>
    /// <param name="type">Action类型</param>
    /// <param name="data">Action数据</param>
    /// <returns>Action响应</returns>
    /// <exception cref="NotImplementedException">未实现的Action</exception>
    public async Task<JObject> Execute(
        String action,
        JObject? data
    )
    {
        try
        {
            var methods = _actionHandler.GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var method in methods)
            {
                var name = method.Name;
                if (name.Contains("Handler") && ToSnakeCase(name.Substring(0, name.Length - 7)) == action)
                {
                    var hasParam = method.GetParameters().Length != 0;
                    var parameterValues = new object?[hasParam ? 1 : 0];
                    if (hasParam)
                    {
                        parameterValues[0] = data;
                    }

                    try
                    {
                        if (data == null) throw new NullReferenceException();
                        Object result = method.Invoke(_actionHandler, parameterValues)!;
                        return ((result is Task<object> task ? await task : result) as JObject)!;
                    }
                    catch (ActionExecutionException e)
                    {
                        return ResponseUtils.Err(action, e);
                    }
                    catch (NullReferenceException)
                    {
                        throw new NullReferenceException("Invalid params");
                    }
                }
            }

            throw new NotImplementedException();
        }
        catch (Exception e)
        {
            return ResponseUtils.Err(e.Message, 1500);
        }
    }
}