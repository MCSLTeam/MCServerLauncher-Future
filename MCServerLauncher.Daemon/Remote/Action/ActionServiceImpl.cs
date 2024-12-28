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
                var result = await actionMethod.InvokeAsync(data);
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
            if (!IsValidActionMethodReturnType(method.ReturnType))
                throw new ArgumentException(
                    $"Action method('{method.Name}') return type({method.ReturnType}) must be JObject(?) or Task<JObject(?)> or ValueTask<JObject(?)>");
            _actionMethods[method.Name[..^7].PascalCaseToSnakeCase()!] = new ActionMethod(_actionHandler, method);
        }
    }

    private static bool IsValidActionMethodReturnType(Type type, bool canBeAsync = true, bool canBeNullable = true)
    {
        if (type == typeof(JObject)) return true;
        if (!type.IsGenericType) return false;

        var outerType = type.GetGenericTypeDefinition();
        var innerType = type.GetGenericArguments()[0];
        if (canBeNullable && outerType == typeof(Nullable<>))
            return IsValidActionMethodReturnType(innerType, canBeAsync, false);
        if ((canBeAsync && outerType == typeof(Task<>)) || outerType == typeof(ValueTask<>))
            return IsValidActionMethodReturnType(innerType, false, canBeNullable);
        return false;
    }
}

internal class ActionMethod
{
    private readonly Func<object?[]?, ValueTask<JObject?>> _method;
    private readonly IDictionary<string, ParameterInfo> _parameters;

    public ActionMethod(object obj, MethodInfo methodInfo)
    {
        _parameters = new Dictionary<string, ParameterInfo>();
        foreach (var parameter in methodInfo.GetParameters())
            _parameters[parameter.Name.PascalCaseToSnakeCase()!] = parameter;
        _method = WarpActionMethod(obj, methodInfo);
    }

    private static Func<object?[]?, ValueTask<JObject?>> WarpActionMethod(object obj, MethodInfo info)
    {
        var retType = info.ReturnType;

        var isValueTask = retType.IsGenericType && retType.GetGenericTypeDefinition() == typeof(ValueTask<>);
        var isAsync = isValueTask || (retType.IsGenericType && retType.GetGenericTypeDefinition() == typeof(Task<>));

        return isValueTask
            ? (Func<object?[]?, ValueTask<JObject?>>)(async parameters =>
                await (ValueTask<JObject?>)info.Invoke(obj, parameters)!)
            : isAsync
                ? (Func<object?[]?, ValueTask<JObject?>>)(async parameters =>
                    await (Task<JObject?>)info.Invoke(obj, parameters)!)
                : (Func<object?[]?, ValueTask<JObject?>>)(parameters =>
                    new ValueTask<JObject?>(info.Invoke(obj, parameters) as JObject));
    }

    public async ValueTask<JObject?> InvokeAsync(JObject? data)
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
            return await _method(parameters);
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