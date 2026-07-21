using System.Collections.Frozen;
using System.Collections.Immutable;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Plugins;
using MCServerLauncher.Daemon.Plugins.Configuration;
using NuGet.Versioning;

namespace MCServerLauncher.ProtocolTests.Plugins;

public sealed class PluginAdmissionPolicyTests
{
    [Fact]
    public void ParseGrantLevelAcceptsKnownValuesAndDefaultsToMedium()
    {
        Assert.Equal(PluginGrantLevel.None, PluginAdmissionPolicy.ParseGrantLevel("none"));
        Assert.Equal(PluginGrantLevel.Low, PluginAdmissionPolicy.ParseGrantLevel("Low"));
        Assert.Equal(PluginGrantLevel.Medium, PluginAdmissionPolicy.ParseGrantLevel("MEDIUM"));
        Assert.Equal(PluginGrantLevel.High, PluginAdmissionPolicy.ParseGrantLevel("high"));
        Assert.Equal(PluginGrantLevel.Custom, PluginAdmissionPolicy.ParseGrantLevel("custom"));
        Assert.Equal(PluginGrantLevel.Medium, PluginAdmissionPolicy.ParseGrantLevel(null));
        Assert.Equal(PluginGrantLevel.Medium, PluginAdmissionPolicy.ParseGrantLevel("garbage"));
    }

    [Fact]
    public void MediumLevelGrantsOnlyImplementedFeaturesUpToMediumRisk()
    {
        var config = DaemonPluginsConfig.Default; // Medium
        var allowed = PluginAdmissionPolicy.FeaturesAllowedByLevel(PluginGrantLevel.Medium, config);

        // Implemented host features at risk <= Medium:
        // rpc.register, event.publish, instance.query, system.query, storage.private, auth.verify,
        // operation.query, operation.cancel, provisioning.manage.
        Assert.Equal(9, allowed.Length);
        Assert.Contains(allowed, f => f == PluginFeature.RpcRegister);
        Assert.Contains(allowed, f => f == PluginFeature.EventPublish);
        Assert.Contains(allowed, f => f == PluginFeature.InstanceQuery);
        Assert.Contains(allowed, f => f == PluginFeature.SystemQuery);
        Assert.Contains(allowed, f => f == PluginFeature.StoragePrivate);
        Assert.Contains(allowed, f => f == PluginFeature.AuthVerify);
        Assert.Contains(allowed, f => f == PluginFeature.OperationQuery);
        Assert.Contains(allowed, f => f == PluginFeature.OperationCancel);
        Assert.Contains(allowed, f => f == PluginFeature.ProvisioningManage);
        // Unimplemented features are never admitted regardless of risk/level.
        Assert.DoesNotContain(allowed, f => f == PluginFeature.InstanceManage);
        // High-risk implemented features require High grant level.
        Assert.DoesNotContain(allowed, f => f == PluginFeature.NetworkHttpListen);
    }

    [Fact]
    public void HighLevelAdmitsImplementedNetworkHttpListen()
    {
        var config = DaemonPluginsConfig.Default;
        var allowed = PluginAdmissionPolicy.FeaturesAllowedByLevel(PluginGrantLevel.High, config);
        Assert.Contains(allowed, f => f == PluginFeature.NetworkHttpListen);
    }

    [Fact]
    public void CustomLevelUsesFeatureGrantsInsteadOfRisk()
    {
        var config = new DaemonPluginsConfig
        {
            GrantLevel = "Custom",
            FeatureGrants = ["instance.query"],
        };
        var allowed = PluginAdmissionPolicy.FeaturesAllowedByLevel(PluginGrantLevel.Custom, config);
        var only = Assert.Single(allowed);
        Assert.Equal(PluginFeature.InstanceQuery, only);
    }

    [Fact]
    public void DecideGrantsAllRequiredWhenWithinCeiling()
    {
        var manifest = Manifest("mcp", [PluginFeature.InstanceQuery, PluginFeature.RpcRegister]);
        var grant = PluginAdmissionPolicy.Decide(manifest, DaemonPluginsConfig.Default);

        Assert.True(grant.Enabled);
        Assert.Empty(grant.Denied);
        Assert.Equal(2, grant.Granted.Length);
    }

    [Fact]
    public void DecideDeniesRequiredFeaturesOutsideCeiling()
    {
        // network.http.listen is High risk; Medium ceiling denies it without plugin_grants.
        var manifest = Manifest("mcp", [PluginFeature.InstanceQuery, PluginFeature.NetworkHttpListen]);
        var grant = PluginAdmissionPolicy.Decide(manifest, DaemonPluginsConfig.Default);

        Assert.True(grant.Enabled);
        var denied = Assert.Single(grant.Denied);
        Assert.Equal(PluginFeature.NetworkHttpListen, denied);
        Assert.Single(grant.Granted);
    }

    [Fact]
    public void PluginGrantsExpandEffectiveSetBeyondCeiling()
    {
        var config = new DaemonPluginsConfig
        {
            GrantLevel = "Medium",
            PluginGrants = { ["mcp"] = ["network.http.listen"] },
        };
        // Even though the feature is granted by plugin_grants, Decide checks membership of the
        // effective set, not IsImplemented. Admission of unimplemented features is handled earlier
        // by PluginManifestReader (feature_unimplemented). Here we only assert the grant decision.
        var manifest = Manifest("mcp", [PluginFeature.NetworkHttpListen]);
        var grant = PluginAdmissionPolicy.Decide(manifest, config);

        Assert.True(grant.Enabled);
        Assert.Empty(grant.Denied);
        Assert.Single(grant.Granted);
    }

    [Fact]
    public void DisabledEntrySkipsPlugin()
    {
        var config = new DaemonPluginsConfig
        {
            GrantLevel = "Medium",
            Entries = { ["mcp"] = new PluginEntryConfig { Enabled = false } },
        };
        var manifest = Manifest("mcp", [PluginFeature.InstanceQuery]);
        var grant = PluginAdmissionPolicy.Decide(manifest, config);

        Assert.False(grant.Enabled);
    }

    [Fact]
    public void ComputeDigestIsDeterministicForSameContent()
    {
        var a = PluginAdmissionPolicy.ComputeDigest("{\"a\":1}");
        var b = PluginAdmissionPolicy.ComputeDigest("{\"a\":1}");
        var c = PluginAdmissionPolicy.ComputeDigest("{\"a\":2}");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(64, a.Length); // SHA-256 hex
    }

    [Fact]
    public void AdmissionMatchesReturnsFalseOnDigestOrFeatureDrift()
    {
        var digest = PluginAdmissionPolicy.ComputeDigest("{}");
        var features = new HashSet<PluginFeature> { PluginFeature.InstanceQuery };

        var matching = new PluginAdmissionConfig
        {
            Decision = "allow",
            ManifestDigest = digest,
            Features = ["instance.query"],
        };
        Assert.True(PluginAdmissionPolicy.AdmissionMatches(matching, digest, features.ToFrozenSet()));

        var driftedDigest = new PluginAdmissionConfig
        {
            Decision = "allow",
            ManifestDigest = "deadbeef",
            Features = ["instance.query"],
        };
        Assert.False(PluginAdmissionPolicy.AdmissionMatches(driftedDigest, digest, features.ToFrozenSet()));

        var driftedFeatures = new PluginAdmissionConfig
        {
            Decision = "allow",
            ManifestDigest = digest,
            Features = ["rpc.register"],
        };
        Assert.False(PluginAdmissionPolicy.AdmissionMatches(driftedFeatures, digest, features.ToFrozenSet()));

        var denied = new PluginAdmissionConfig
        {
            Decision = "deny",
            ManifestDigest = digest,
            Features = ["instance.query"],
        };
        Assert.False(PluginAdmissionPolicy.AdmissionMatches(denied, digest, features.ToFrozenSet()));
    }

    [Fact]
    public void HttpEndpointRegistryRejectsDuplicateIpPortAndReleasesOnStop()
    {
        var registry = new PluginHttpEndpointRegistry();
        var ep = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 11453);

        Assert.True(registry.TryRegister("mcp", ep, out _));
        Assert.False(registry.TryRegister("other", ep, out var conflict));
        Assert.Equal("mcp", conflict);

        registry.Release("mcp");
        Assert.True(registry.TryRegister("other", ep, out _));
    }

    [Fact]
    public void HttpEndpointRegistryRejectsSamePortAcrossDifferentAddresses()
    {
        // A daemon binding 0.0.0.0:port occupies the port on every address, so a plugin binding
        // 127.0.0.1:port must be rejected too. The conflict key is the PORT, not the IP.
        var registry = new PluginHttpEndpointRegistry();
        Assert.True(registry.TryRegister(
            "mcsl.daemon",
            new System.Net.IPEndPoint(System.Net.IPAddress.Any, 11452),
            out _));
        Assert.False(registry.TryRegister(
            "mcp",
            new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 11452),
            out var conflict));
        Assert.Equal("mcsl.daemon", conflict);
    }

    private static PluginManifest Manifest(string id, PluginFeature[] features)
    {
        var frozen = features.ToFrozenSet();
        return new PluginManifest(
            new PluginIdentity(id, "1.0.0"),
            "PluginEntry.dll",
            "PluginEntry",
            NuGetVersion.Parse("1.0.0"),
            VersionRange.Parse("[2.0.0,3.0.0)"),
            frozen,
            "/bundle",
            "/bundle/PluginEntry.dll",
            "digest");
    }
}
