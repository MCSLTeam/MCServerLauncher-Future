using System.Reflection;
using MCServerLauncher.Daemon.Utils;
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
    private readonly IDictionary<string, ActionMethod> _actionMethods;

    public ActionServiceImpl(ActionHandler handler)
    {
        _actionHandler = handler;
        _actionMethods = new Dictionary<string, ActionMethod>();
        InitActionMethods();
    }

    /// <summary>
    ///     Action(rpc)处理中枢
    /// </summary>
    /// <param name="action">Action类型</param>
    /// <param name="data">Action数据</param>
    /// <returns>Action响应</returns>
    public async Task<JObject> Execute(
        string action,
        JObject? data
    )
    {
        if (_actionMethods.TryGetValue(action, out var actionMethod))
            try
            {
                var result = await actionMethod.InvokeAsync(_actionHandler, data);
                return ResponseUtils.Ok(result);
            }
            catch (ActionExecutionException aee)
            {
                return ResponseUtils.Err(action, aee);
            }
            catch (Exception e)
            {
                return ResponseUtils.Err(action, e.Message, 1500);
            }

        return ResponseUtils.Err(action, $"Action '{action}' not found");
    }

    // TODO :ActionHandler中添加JObject?返回值
    private void InitActionMethods()
    {
        var methods = _actionHandler.GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => m.Name.EndsWith("Handler"));
        foreach (var method in methods)
        {
            // validate method return type
            if (!IsValidActionMethod(method))
                throw new ArgumentException(
                    $"Action method('{method.Name}') return type must be JObject or Task<JObject>");
            _actionMethods[method.Name[..^7].PascalCaseToSnakeCase()!] = new ActionMethod(method);
        }
    }

    private static bool IsValidActionMethod(MethodInfo methodInfo)
    {
        var returnType = methodInfo.ReturnType;
        if (returnType == typeof(JObject)) return true;
        if (returnType.IsGenericType)
        {
            var genericTypeDefinition = returnType.GetGenericTypeDefinition();
            if (genericTypeDefinition != typeof(Task<>) && genericTypeDefinition != typeof(Nullable<>))
                return false;
            return returnType.GetGenericArguments()[0] == typeof(JObject);
        }

        return false;
    }
}

internal class ActionMethod
{
    private readonly bool _async;
    private readonly MethodInfo _methodInfo;
    private readonly IDictionary<string, ParameterInfo> _parameters;

    public ActionMethod(MethodInfo methodInfo)
    {
        _methodInfo = methodInfo;
        _parameters = new Dictionary<string, ParameterInfo>();
        foreach (var parameter in methodInfo.GetParameters())
            _parameters[parameter.Name.PascalCaseToSnakeCase()!] = parameter;

        var returnType = methodInfo.ReturnType;
        _async = returnType.IsGenericType &&
                 returnType.GetGenericTypeDefinition() ==
                 typeof(Task<>); // ActionHandler中的方法只能返回Task<JObject>或者JObject, 且在构造方法中检查过
    }

    public async ValueTask<JObject?> InvokeAsync(object obj, JObject? data)
    {
        var parameters = _parameters.Count > 0 ? new object?[_parameters.Count] : null;
        foreach (var entry in _parameters)
        {
            var parameterToken = data?.GetValue(entry.Key);
            if (parameterToken is null) throw new ArgumentNullException(entry.Key);

            var parameter = parameterToken.ToObject(entry.Value.ParameterType);
            parameters![entry.Value.Position] = parameter;
        }

        try
        {
            if (_async) return await (_methodInfo.Invoke(obj, parameters) as Task<JObject>)!;
            ;


            return _methodInfo.Invoke(obj, parameters) as JObject;
        }
        catch (TargetInvocationException e)
        {
            if (e.InnerException is ActionExecutionException ex)
                throw ex;
            if (e.InnerException != null)
                throw e.InnerException;
            throw;
        }
    }
}