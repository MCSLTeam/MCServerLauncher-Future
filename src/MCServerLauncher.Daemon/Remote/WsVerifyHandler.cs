using MCServerLauncher.Daemon.Remote.Authentication;
using Serilog;
using TouchSocket.Http;

namespace MCServerLauncher.Daemon.Remote;

public static class WsVerifyHandler
{
    public static bool RejectWithNoReason { get; set; } = false;

    public static async Task<bool> VerifyV2Handler(IHttpSessionClient client, HttpContext context)
    {
        if (!StringComparer.Ordinal.Equals(context.Request.RelativeURL, "/api/v2")) return false;
        return await VerifyTokenAsync(context).ConfigureAwait(false);
    }

    private static async Task<bool> VerifyTokenAsync(HttpContext context)
    {
        if (RejectWithNoReason)
        {
            await context.Response.SetStatus(403, "Daemon rejected").AnswerAsync();
            return false;
        }

        try
        {
            if (context.Request.Query.TryGetValue("token", out var token) && JwtUtils.ValidateToken(token)) return true;

            await context.Response.SetStatus(401, "Unauthorized").AnswerAsync();
            return false;
        }
        catch (Exception e)
        {
            Log.Error(e, "[WsVerifyHandler] Verify failed");
            await context.Response.SetStatus(500, "Internal Server Error").AnswerAsync();
            return false;
        }
    }
}
