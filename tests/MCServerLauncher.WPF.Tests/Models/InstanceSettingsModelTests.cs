using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.WPF.InstanceConsole.ViewModels.Models;

namespace MCServerLauncher.WPF.Tests.Models;

public sealed class InstanceSettingsModelTests
{
    [Fact]
    public void InstallMetadata_UsesCanonicalContractAndPreservesImmutableGeneratedPaths()
    {
        var generatedPaths = ImmutableArray.Create("libraries/example.jar", "run/start.bat");
        var metadata = new InstanceInstallMetadata(
            "Forge",
            "installers/forge-installer.jar",
            generatedPaths,
            "run/start.bat",
            DateTimeOffset.Parse("2026-07-14T00:00:00+00:00"));
        var model = new InstanceSettingsModel { InstallMetadata = metadata };

        var property = typeof(InstanceSettingsModel).GetProperty(nameof(InstanceSettingsModel.InstallMetadata));

        Assert.NotNull(property);
        Assert.Equal(typeof(InstanceInstallMetadata), property.PropertyType);
        Assert.Same(metadata, model.InstallMetadata);
        Assert.Equal("Forge", model.InstallMetadata!.InstallerKind);
        Assert.Equal("installers/forge-installer.jar", model.InstallMetadata.InstallerSourcePath);
        Assert.Equal(generatedPaths, model.InstallMetadata.GeneratedPaths);
        Assert.Equal("run/start.bat", model.InstallMetadata.ResolvedLaunchTarget);
    }
}
