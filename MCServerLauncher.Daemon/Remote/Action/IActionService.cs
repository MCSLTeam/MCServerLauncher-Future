using MCServerLauncher.Daemon.Remote.Authentication;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     Action处理接口
/// </summary>
public interface IActionService
{
    public Task<JObject> Execute(string action, JObject? data, Permissions permissions);
}