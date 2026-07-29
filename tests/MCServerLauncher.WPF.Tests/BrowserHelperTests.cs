using MCServerLauncher.WPF.Modules;

namespace MCServerLauncher.WPF.Tests;

public sealed class BrowserHelperTests
{
    [Fact]
    public void CreateBrowserStartInfoUsesShellExecuteForUrl()
    {
        const string url = "https://afdian.com/a/bangbang93/";

        var startInfo = BrowserHelper.CreateBrowserStartInfo(url);

        Assert.Equal(url, startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
    }
}