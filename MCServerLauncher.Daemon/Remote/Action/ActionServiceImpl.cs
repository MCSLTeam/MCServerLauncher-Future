using System.Reflection;
using MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Exception = System.Exception;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     Action(rpc)处理器。
///     Action是ws交互格式的一种，他代表的是一个远程过程调用: C->S,处理后返回json数据给C
/// </summary>
// internal class ActionServiceImpl : IActionService
// {
//     private readonly ActionHandlers _actionHandlers;
//     private readonly IDictionary<string, ActionMethod> _actionMethods;
//
//     public ActionServiceImpl(ActionHandlers handlers, IWebJsonConverter webJsonConverter)
//     {
//         _actionHandlers = handlers;
//         _actionMethods = new Dictionary<string, ActionMethod>();
//
//         InitActionMethods(webJsonConverter.GetSerializer());
//     }
//
//     /// <summary>
//     ///     Action(rpc)处理中枢
//     /// </summary>
//     /// <param name="action">Action类型</param>
//     /// <param name="data">Action数据</param>
//     /// <param name="context">websocket服务上下文</param>
//     /// <returns>Action响应</returns>
//     public async Task<JObject> ProcessAsync(
//         string action,
//         JObject? data,
//         WsServiceContext context
//     )
//     {
//         if (_actionMethods.TryGetValue(action, out var actionMethod))
//             try
//             {
//                 // if (!permissions.Matches(
//                 //         ActionHandlers.RequiredPermissions.GetValueOrDefault(action, new AlwaysMatchable(true))))
//                 //     throw new Exception("Permission denied");
//                 var result = await actionMethod.InvokeAsync(data, context);
//                 return ResponseUtils.Ok(result);
//             }
//             catch (ActionExecutionException aee)
//             {
//                 return ResponseUtils.Err(action, aee);
//             }
//             catch (Exception e)
//             {
//                 return ResponseUtils.Err(action, e.Message, 1500);
//             }
//
//         return ResponseUtils.Err(action, $"Action '{action}' not found");
//     }
//
//     // TODO :ActionHandler中添加JObject?返回值
//     /// <summary>
//     ///     使用反射初始化Action方法并缓存; 通过Attribute设置Action权限
//     /// </summary>
//     /// <exception cref="ArgumentException"></exception>
//     private void InitActionMethods(JsonSerializer parameterSerializer)
//     {
//         var methods = _actionHandlers.GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
//             .Where(m => m.Name.EndsWith("Handler"));
//         foreach (var method in methods)
//         {
//             // validate method return type
//             if (!IsValidActionMethodReturnType(method.ReturnType))
//                 throw new ArgumentException(
//                     $"Action method('{method.Name}') return type({method.ReturnType}) must be JObject(?) or Task<JObject(?)> or ValueTask<JObject(?)>");
//
//             var methodPermissions = method.GetCustomAttributes(false)
//                 .Where(attr => attr is IPermissionAttribute)
//                 .OfType<IPermissionAttribute>()
//                 .Select(attr => attr.GetPermission()).ToArray();
//
//             _actionMethods[method.Name[..^7].PascalCaseToSnakeCase()!] =
//                 new ActionMethod(
//                     _actionHandlers,
//                     method,
//                     methodPermissions.Length != 0 ? IMatchable.Any(methodPermissions) : IMatchable.Never(),
//                     parameterSerializer
//                 );
//         }
//     }
//
//     private static bool IsValidActionMethodReturnType(Type type, bool canBeAsync = true, bool canBeNullable = true)
//     {
//         if (type == typeof(JObject)) return true;
//         if (!type.IsGenericType) return false;
//
//         var outerType = type.GetGenericTypeDefinition();
//         var innerType = type.GetGenericArguments()[0];
//         if (canBeNullable && outerType == typeof(Nullable<>))
//             return IsValidActionMethodReturnType(innerType, canBeAsync, false);
//         if ((canBeAsync && outerType == typeof(Task<>)) || outerType == typeof(ValueTask<>))
//             return IsValidActionMethodReturnType(innerType, false, canBeNullable);
//         return false;
//     }
// }
internal class ActionMethod
{
    private readonly Func<object?[]?, ValueTask<JObject?>> _method;
    private readonly IDictionary<string, ParameterInfo> _parameters;
    private readonly JsonSerializer _parameterSerializer;
    private readonly IMatchable _permission;

    /// <summary>
    ///     ActionMethod构造函数
    /// </summary>
    /// <param name="obj">method所绑定的obj,即<see cref="ActionHandlers" />实例</param>
    /// <param name="methodInfo">反射获取的Action Handler信息</param>
    /// <param name="permission">Action Handler的调用权限</param>
    /// <param name="parameterSerializer">参数序列化器</param>
    public ActionMethod(object obj, MethodInfo methodInfo, IMatchable permission, JsonSerializer parameterSerializer)
    {
        _parameters = new Dictionary<string, ParameterInfo>();
        _permission = permission;
        _parameterSerializer = parameterSerializer;
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
            ? async parameters =>
                await (ValueTask<JObject?>)info.Invoke(obj, parameters)!
            : isAsync
                ? async parameters =>
                    await (Task<JObject?>)info.Invoke(obj, parameters)!
                : parameters =>
                    new ValueTask<JObject?>(info.Invoke(obj, parameters) as JObject);
    }

    public async ValueTask<JObject?> InvokeAsync(JObject? data, WsServiceContext context)
    {
        if (!context.Permissions.Matches(_permission)) throw new Exception("Permission denied");


        var parameters = _parameters.Count > 0 ? new object?[_parameters.Count] : null;
        foreach (var entry in _parameters)
        {
            // 注入WsServiceContext
            if (entry.Value.ParameterType == typeof(WsServiceContext))
            {
                parameters![entry.Value.Position] = context;
                continue;
            }

            // JToken直接获取原始数据

            var parameterToken = data?.GetValue(entry.Key);
            if (parameterToken is null) throw new ArgumentNullException(entry.Key);

            if (entry.Value.ParameterType == typeof(JToken)) parameters![entry.Value.Position] = parameterToken;
            else
                parameters![entry.Value.Position] =
                    parameterToken.ToObject(entry.Value.ParameterType, _parameterSerializer);
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