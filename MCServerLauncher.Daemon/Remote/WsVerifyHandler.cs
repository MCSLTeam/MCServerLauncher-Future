using MCServerLauncher.Daemon.Remote.Authentication;
using TouchSocket.Http;

namespace MCServerLauncher.Daemon.Remote;

public static class WsVerifyHandler
{
    public static bool RejectWithNoReason { get; set; } = false;

    public static async Task<bool> VerifyHandler(IHttpSessionClient client, HttpContext context)
    {
        if (!context.Request.URL.StartsWith("/api/v1")) return false;
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
            System.Console.WriteLine(e);
            await context.Response.SetStatus(500, e.Message).AnswerAsync();
            return false;
        }
    }
}