namespace MCServerLauncher.Daemon;

public static class Program
{
    private static async Task Main(string[] args)
    {
        if (!Application.Init()) Environment.Exit(1);

        var app = new Application();
        await app.ServeAsync();
    }
}