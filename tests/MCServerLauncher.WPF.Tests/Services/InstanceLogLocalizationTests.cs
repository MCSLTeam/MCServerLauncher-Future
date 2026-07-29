using System.Globalization;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;

namespace MCServerLauncher.WPF.Tests;

public class InstanceLogLocalizationTests
{
    [Fact]
    public void TryLocalize_OnlyKeepsStartingAndStoppedLifecycleMessages()
    {
        var lifecycleMessages = new[]
        {
            "[MCSL] Instance starting.",
            "[MCSL] Instance stopped.",
            "[MCSL] Instance running.",
            "[MCSL] Instance stopping.",
            "[MCSL] Instance crashed."
        };

        var localized = lifecycleMessages
            .Where(message => InstanceLogLocalization.TryLocalize(message, out _))
            .ToArray();

        Assert.Equal(
            ["[MCSL] Instance starting.", "[MCSL] Instance stopped."],
            localized);
    }

    [Fact]
    public void LocalizeHistory_FiltersRemovedLifecycleMessagesAndWhitespace()
    {
        var localized = InstanceLogLocalization.LocalizeHistory(
        [
            "[MCSL] Instance starting.",
            "[MCSL] Instance running.",
            "server output",
            "   ",
            "[MCSL] Instance stopped."
        ]);

        Assert.Equal(3, localized.Length);
        Assert.Contains("server output", localized);
        Assert.DoesNotContain("[MCSL] Instance running.", localized);
        Assert.DoesNotContain("   ", localized);
    }

    [Fact]
    public void LifecycleResources_AreAvailableForEverySupportedLanguage()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var language in Lang.LanguageList)
            {
                Lang.Tr.ChangeLanguage(CultureInfo.GetCultureInfo(language!));

                Assert.NotEqual("InstanceLog_Starting", Lang.Tr["InstanceLog_Starting"]);
                Assert.NotEqual("InstanceLog_Stopped", Lang.Tr["InstanceLog_Stopped"]);
            }
        }
        finally
        {
            Lang.Tr.ChangeLanguage(originalCulture);
        }
    }
}
