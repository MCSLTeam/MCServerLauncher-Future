using System.Collections.Immutable;
using MCServerLauncher.Daemon.API.Plugins;

namespace MCServerLauncher.Daemon.ApiTests.Plugins;

public sealed class ExtensionPointCatalogTests
{
    [Fact]
    public void CatalogContainsInitialFormalExtensionPoints()
    {
        Assert.True(ExtensionPointCatalog.IsKnown("provider.java"));
        Assert.True(ExtensionPointCatalog.IsKnown("registry.template"));
        Assert.True(ExtensionPointCatalog.IsKnown("override.process.launcher"));
        Assert.False(ExtensionPointCatalog.IsKnown("private.instanceBase.patch"));
    }

    [Fact]
    public void AdmissionAcceptsDeclaredKnownExtensionPoints()
    {
        var result = ExtensionPointCatalog.ValidateDeclarations(
        [
            new PluginExtensionPointDeclaration("provider.java", PluginExtensionPointKind.Provider, Priority: 10),
            new PluginExtensionPointDeclaration("registry.template", PluginExtensionPointKind.Registry)
        ]);

        Assert.True(result.IsAdmitted, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal("admitted", result.Code);
    }

    [Fact]
    public void AdmissionRejectsUnknownOrMismatchedExtensionPoints()
    {
        var result = ExtensionPointCatalog.ValidateDeclarations(
        [
            new PluginExtensionPointDeclaration("private.instanceBase.patch", PluginExtensionPointKind.Override),
            new PluginExtensionPointDeclaration("provider.java", PluginExtensionPointKind.Registry)
        ]);

        Assert.False(result.IsAdmitted);
        Assert.Contains(result.Diagnostics, item => item == "extension_point_unknown:private.instanceBase.patch");
        Assert.Contains(result.Diagnostics, item => item == "extension_point_kind_mismatch:provider.java");
    }

    [Fact]
    public void AdmissionRejectsConflictingDeclaredExtensionPoints()
    {
        var result = ExtensionPointCatalog.ValidateDeclarations(
        [
            new PluginExtensionPointDeclaration(
                "provider.java",
                PluginExtensionPointKind.Provider,
                Conflicts: ["provider.serverCore"]),
            new PluginExtensionPointDeclaration("provider.serverCore", PluginExtensionPointKind.Provider)
        ]);

        Assert.False(result.IsAdmitted);
        Assert.Contains(result.Diagnostics, item => item == "extension_point_conflict:provider.java:provider.serverCore");
    }

    [Fact]
    public void AdmissionAllowsOnlyDeclaredOverridePointsToReplace()
    {
        var denied = ExtensionPointCatalog.ValidateDeclarations(
        [
            new PluginExtensionPointDeclaration("provider.java", PluginExtensionPointKind.Provider, Replaces: "builtin.java")
        ]);
        Assert.False(denied.IsAdmitted);
        Assert.Contains(denied.Diagnostics, item => item == "extension_point_replace_unsupported:provider.java");

        var admitted = ExtensionPointCatalog.ValidateDeclarations(
        [
            new PluginExtensionPointDeclaration("override.process.launcher", PluginExtensionPointKind.Override, Replaces: "builtin.processLauncher")
        ]);
        Assert.True(admitted.IsAdmitted, string.Join(Environment.NewLine, admitted.Diagnostics));
    }
}
