using Newtonsoft.Json.Linq;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     Action处理接口
/// </summary>
public interface IActionService
{
    public Task<JObject> Routine(ActionType type, JObject? data);
    JObject Err(string? message, int code = 1400);
    JObject Ok(JObject? data = null);
}