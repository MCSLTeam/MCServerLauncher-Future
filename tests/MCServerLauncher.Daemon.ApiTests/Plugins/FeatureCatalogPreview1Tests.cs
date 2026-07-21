using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
namespace MCServerLauncher.Daemon.ApiTests.Plugins;

public sealed class FeatureCatalogPreview1Tests
{
    [Fact]
    public void Preview1ImplementedFeaturesMatchDecisionSpecFreeze()
    {
        string[] expectedImplemented =
        [
            PluginFeature.SystemQuery.Value,
            PluginFeature.InstanceQuery.Value,
            PluginFeature.InstanceManage.Value,
            PluginFeature.OperationQuery.Value,
            PluginFeature.OperationCancel.Value,
            PluginFeature.ProvisioningManage.Value,
            PluginFeature.NetworkHttpListen.Value,
            PluginFeature.AuthVerify.Value,
            PluginFeature.StoragePrivate.Value,
            // Host infrastructure features that are implemented for generated plugins.
            PluginFeature.RpcRegister.Value,
            PluginFeature.EventPublish.Value,
        ];

        var implemented = FeatureCatalog.All
            .Where(static descriptor => descriptor.IsImplemented)
            .Select(static descriptor => descriptor.Feature.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedImplemented.Order(StringComparer.Ordinal).ToArray(), implemented);

        Assert.Equal(
            [
                "mcsl.java.list",
                "mcsl.system.info.get",
            ],
            FeatureCatalog.MethodsFor(PluginFeature.SystemQuery).Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                "mcsl.instance.catalog.get",
                "mcsl.instance.log.get",
                "mcsl.instance.report.get",
                "mcsl.instance.report.list",
                "mcsl.instance.settings.get",
            ],
            FeatureCatalog.MethodsFor(PluginFeature.InstanceQuery).Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                "mcsl.instance.command.send",
                "mcsl.instance.create",
                "mcsl.instance.halt",
                "mcsl.instance.remove",
                "mcsl.instance.settings.update",
                "mcsl.instance.start",
                "mcsl.instance.stop",
            ],
            FeatureCatalog.MethodsFor(PluginFeature.InstanceManage).Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                "mcsl.operation.get",
                "mcsl.operation.list",
            ],
            FeatureCatalog.MethodsFor(PluginFeature.OperationQuery).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["mcsl.operation.cancel"],
            FeatureCatalog.MethodsFor(PluginFeature.OperationCancel).Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                "mcsl.provisioning.execute",
                "mcsl.provisioning.get",
                "mcsl.provisioning.resolve",
            ],
            FeatureCatalog.MethodsFor(PluginFeature.ProvisioningManage).Order(StringComparer.Ordinal));
        Assert.Empty(FeatureCatalog.MethodsFor(PluginFeature.AuthVerify));
        Assert.Empty(FeatureCatalog.MethodsFor(PluginFeature.NetworkHttpListen));
        Assert.Empty(FeatureCatalog.MethodsFor(PluginFeature.StoragePrivate));

        // Preview-2 domains remain unimplemented and contribute nothing to host expansion.
        Assert.False(FeatureCatalog.IsImplemented(PluginFeature.BackupManage));
        Assert.False(FeatureCatalog.IsImplemented(PluginFeature.MonitoringQuery));
        Assert.False(FeatureCatalog.IsImplemented(PluginFeature.AutomationManage));
        Assert.False(FeatureCatalog.IsImplemented(PluginFeature.AuditQuery));
        Assert.False(FeatureCatalog.IsImplemented(PluginFeature.FileRead));
        Assert.False(FeatureCatalog.IsImplemented(PluginFeature.FileWrite));
        Assert.False(FeatureCatalog.IsImplemented(PluginFeature.EventSubscribe));
        Assert.False(FeatureCatalog.IsImplemented(PluginFeature.EventRuleManage));
    }
}
