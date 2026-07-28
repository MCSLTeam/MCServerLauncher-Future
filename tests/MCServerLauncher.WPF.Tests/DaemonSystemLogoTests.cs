using MCServerLauncher.WPF.ViewModels;

namespace MCServerLauncher.WPF.Tests;

public sealed class DaemonSystemLogoTests
{
    [Theory]
    [InlineData("Windows", "GenuineIntel", "Windows")]
    [InlineData("Microsoft Windows 11", "GenuineIntel", "Windows")]
    [InlineData("Darwin", "Apple", "Darwin")]
    [InlineData("macOS", "Apple", "Darwin")]
    [InlineData("Ubuntu 24.04", "AuthenticAMD", "Linux")]
    [InlineData("Unknown", "", "Linux")]
    public void ClassifySystemTypeAlwaysMapsToAnAvailableLogo(string systemName, string cpuVendor, string expected)
    {
        Assert.Equal(expected, DaemonManagerViewModel.ClassifySystemType(systemName, cpuVendor));
    }
}
