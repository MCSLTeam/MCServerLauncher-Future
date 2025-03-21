using Newtonsoft.Json.Linq;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     Action处理接口
/// </summary>
public interface IActionService
{
    public Task<JObject> ProcessAsync(JObject data, IResolver resolver, CancellationToken cancellationToken);
}