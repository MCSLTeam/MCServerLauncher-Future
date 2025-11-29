namespace MCServerLauncher.Daemon;

public static class Program
{
    private static async Task Main(string[] args)
    {
        if (!await Application.InitAsync()) Environment.Exit(1);

        await Application.SetupAsync();
        await Application.ServeAsync();
    }
}