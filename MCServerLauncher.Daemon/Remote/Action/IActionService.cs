using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     Action处理接口
/// </summary>
public interface IActionService
{
    public Task<Dictionary<string, object>> Routine(ActionType type, JObject? data);
    Dictionary<string, object> Err(string? message, int code = 1400);
    Dictionary<string, object> Ok(Actions.IActionResponse? data = null);
}