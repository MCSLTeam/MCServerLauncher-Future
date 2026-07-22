using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon;
using MCServerLauncher.Daemon.Plugins;
using MCServerLauncher.Daemon.Plugins.Configuration;
using NuGet.Versioning;

namespace MCServerLauncher.ProtocolTests.Plugins;

public sealed class PluginAdmissionPolicyTests
{
    [Fact]
    public void ParseGrantLevelAcceptsKnownValuesAndRejectsInvalidValues()
    {
        Assert.Equal(PluginGrantLevel.None, PluginAdmissionPolicy.ParseGrantLevel("none"));
        Assert.Equal(PluginGrantLevel.Low, PluginAdmissionPolicy.ParseGrantLevel("Low"));
        Assert.Equal(PluginGrantLevel.Medium, PluginAdmissionPolicy.ParseGrantLevel("MEDIUM"));
        Assert.Equal(PluginGrantLevel.High, PluginAdmissionPolicy.ParseGrantLevel("high"));
        Assert.Equal(PluginGrantLevel.Custom, PluginAdmissionPolicy.ParseGrantLevel("custom"));
        Assert.Throws<ArgumentException>(() => PluginAdmissionPolicy.ParseGrantLevel(null));
        Assert.Throws<ArgumentException>(() => PluginAdmissionPolicy.ParseGrantLevel("garbage"));
        Assert.Throws<ArgumentException>(() => PluginAdmissionPolicy.ParseGrantLevel(" Medium "));
    }

    [Fact]
    public void MediumLevelGrantsOnlyImplementedFeaturesUpToMediumRisk()
    {
        var config = DaemonPluginsConfig.Default; // Medium
        var allowed = PluginAdmissionPolicy.FeaturesAllowedByLevel(PluginGrantLevel.Medium, config);

        // Implemented host features at risk <= Medium:
        // rpc.register, event.publish, instance.query, system.query, storage.private, auth.verify,
        // operation.query, operation.cancel, provisioning.manage, instance.manage.
        Assert.Equal(10, allowed.Length);
        Assert.Contains(allowed, f => f == PluginFeature.RpcRegister);
        Assert.Contains(allowed, f => f == PluginFeature.EventPublish);
        Assert.Contains(allowed, f => f == PluginFeature.InstanceQuery);
        Assert.Contains(allowed, f => f == PluginFeature.SystemQuery);
        Assert.Contains(allowed, f => f == PluginFeature.StoragePrivate);
        Assert.Contains(allowed, f => f == PluginFeature.AuthVerify);
        Assert.Contains(allowed, f => f == PluginFeature.OperationQuery);
        Assert.Contains(allowed, f => f == PluginFeature.OperationCancel);
        Assert.Contains(allowed, f => f == PluginFeature.ProvisioningManage);
        Assert.Contains(allowed, f => f == PluginFeature.InstanceManage);
        // Unimplemented features are never admitted regardless of risk/level.
        Assert.DoesNotContain(allowed, f => f == PluginFeature.BackupManage);
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
    public void AdmissionMatchesReturnsFalseOnDigestOrFeatureDrift()
    {
        const string digest = "current-digest";
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
    public void PreflightSilentlyAdmitsFeaturesWithinCeiling()
    {
        var console = new FakePreflightConsole(isInteractive: true, PluginPreflightDecision.Deny);
        var preflight = new PluginAdmissionPreflight(DaemonPluginsConfig.Default, console);

        var outcome = preflight.Evaluate(Manifest("mcp", [PluginFeature.InstanceQuery]));

        Assert.True(outcome.IsAdmitted);
        Assert.Equal("within_ceiling", outcome.Code);
        Assert.Equal(0, console.PromptCount);
    }

    [Theory]
    [InlineData((int)PluginPreflightDecision.Deny, false, "approval_denied", 0)]
    [InlineData((int)PluginPreflightDecision.Approve, true, "approved_for_process", 0)]
    [InlineData((int)PluginPreflightDecision.ApprovePermanent, true, "approved_permanently", 1)]
    public void InteractivePreflightAppliesAllThreeDecisions(
        int decisionValue,
        bool admitted,
        string expectedCode,
        int expectedStoreCalls)
    {
        var console = new FakePreflightConsole(isInteractive: true, (PluginPreflightDecision)decisionValue);
        var store = new FakeAdmissionStore(succeeds: true);
        var preflight = new PluginAdmissionPreflight(DaemonPluginsConfig.Default, console, store);

        var outcome = preflight.Evaluate(Manifest("mcp", [PluginFeature.NetworkHttpListen]));

        Assert.Equal(admitted, outcome.IsAdmitted);
        Assert.Equal(expectedCode, outcome.Code);
        Assert.Equal(1, console.PromptCount);
        Assert.Equal(expectedStoreCalls, store.CallCount);
    }

    [Fact]
    public void NonInteractivePreflightSkipsFeaturesOutsideCeiling()
    {
        var console = new FakePreflightConsole(isInteractive: false, PluginPreflightDecision.Approve);
        var preflight = new PluginAdmissionPreflight(DaemonPluginsConfig.Default, console);

        var outcome = preflight.Evaluate(Manifest("mcp", [PluginFeature.NetworkHttpListen]));

        Assert.False(outcome.IsAdmitted);
        Assert.Equal("approval_required_non_interactive", outcome.Code);
        Assert.Equal(0, console.PromptCount);
    }

    [Fact]
    public void MatchingPermanentAdmissionIsReusedWithoutPrompt()
    {
        var manifest = Manifest("mcp", [PluginFeature.NetworkHttpListen], "digest-current");
        var config = new DaemonPluginsConfig
        {
            PluginGrants = { ["mcp"] = ["network.http.listen"] },
            Admissions =
            {
                ["mcp"] = new PluginAdmissionConfig
                {
                    Decision = "allow",
                    ManifestDigest = manifest.ManifestDigest,
                    Features = ["network.http.listen"],
                    DecidedAt = "2026-07-22T00:00:00.0000000+00:00",
                },
            },
        };
        var console = new FakePreflightConsole(isInteractive: true, PluginPreflightDecision.Deny);
        var preflight = new PluginAdmissionPreflight(config, console);

        var outcome = preflight.Evaluate(manifest);

        Assert.True(outcome.IsAdmitted);
        Assert.Equal("permanent_admission_reused", outcome.Code);
        Assert.Equal(0, console.PromptCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PermanentAdmissionDigestOrFeatureDriftRequiresReview(bool driftDigest)
    {
        var manifest = Manifest(
            "mcp",
            [PluginFeature.InstanceQuery, PluginFeature.NetworkHttpListen],
            "digest-current");
        var config = new DaemonPluginsConfig
        {
            PluginGrants = { ["mcp"] = ["network.http.listen"] },
            Admissions =
            {
                ["mcp"] = new PluginAdmissionConfig
                {
                    Decision = "allow",
                    ManifestDigest = driftDigest ? "digest-stale" : manifest.ManifestDigest,
                    Features = driftDigest
                        ? ["instance.query", "network.http.listen"]
                        : ["network.http.listen"],
                },
            },
        };
        var console = new FakePreflightConsole(isInteractive: false, PluginPreflightDecision.Approve);
        var preflight = new PluginAdmissionPreflight(config, console);

        var outcome = preflight.Evaluate(manifest);

        Assert.False(outcome.IsAdmitted);
        Assert.Equal("admission_drift_non_interactive", outcome.Code);
        Assert.Equal(0, console.PromptCount);
    }

    [Fact]
    public void PermanentAdmissionDigestDriftWithinCeilingStillRequiresReview()
    {
        var manifest = Manifest("mcp", [PluginFeature.InstanceQuery], "digest-current");
        var config = new DaemonPluginsConfig
        {
            Admissions =
            {
                ["mcp"] = new PluginAdmissionConfig
                {
                    Decision = "allow",
                    ManifestDigest = "digest-stale",
                    Features = ["instance.query"],
                },
            },
        };
        var console = new FakePreflightConsole(isInteractive: false, PluginPreflightDecision.Approve);
        var preflight = new PluginAdmissionPreflight(config, console);

        var outcome = preflight.Evaluate(manifest);

        Assert.False(outcome.IsAdmitted);
        Assert.Equal("admission_drift_non_interactive", outcome.Code);
        Assert.Equal(0, console.PromptCount);
    }

    [Fact]
    public void PermanentApprovalPersistenceFailureSkipsPlugin()
    {
        var console = new FakePreflightConsole(isInteractive: true, PluginPreflightDecision.ApprovePermanent);
        var store = new FakeAdmissionStore(succeeds: false);
        var preflight = new PluginAdmissionPreflight(DaemonPluginsConfig.Default, console, store);

        var outcome = preflight.Evaluate(Manifest("mcp", [PluginFeature.NetworkHttpListen]));

        Assert.False(outcome.IsAdmitted);
        Assert.Equal("permanent_admission_persist_failed", outcome.Code);
        Assert.Equal(1, store.CallCount);
    }

    [Fact]
    public void AppConfigPermanentAdmissionPersistsAtomicallyAndRollsBackOnFailure()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plugin-admission-").FullName;
        try
        {
            var config = new DaemonPluginsConfig();
            var appConfig = new AppConfig(11452, "secret", "main-token", plugins: config);
            var configPath = Path.Combine(root, "config.json");
            Assert.True(appConfig.TrySave(configPath));
            var manifest = Manifest("mcp", [PluginFeature.NetworkHttpListen], "digest-current");
            var store = new AppConfigPluginAdmissionStore(appConfig, configPath);

            Assert.True(store.TryPersistPermanent(
                manifest,
                [PluginFeature.NetworkHttpListen],
                DateTimeOffset.Parse("2026-07-22T01:02:03Z", CultureInfo.InvariantCulture)));

            using (var document = JsonDocument.Parse(File.ReadAllText(configPath)))
            {
                var plugins = document.RootElement.GetProperty("plugins");
                Assert.Equal(
                    "network.http.listen",
                    plugins.GetProperty("plugin_grants").GetProperty("mcp")[0].GetString());
                Assert.Equal(
                    manifest.ManifestDigest,
                    plugins.GetProperty("admissions").GetProperty("mcp").GetProperty("manifest_digest").GetString());
            }

            Assert.True(File.Exists(configPath + ".bak"));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.TopDirectoryOnly));

            var failingConfig = new DaemonPluginsConfig();
            var failingAppConfig = new AppConfig(11452, "secret", "main-token", plugins: failingConfig);
            var failingStore = new AppConfigPluginAdmissionStore(
                failingAppConfig,
                Path.Combine(root, "missing", "config.json"));
            Assert.False(failingStore.TryPersistPermanent(
                manifest,
                [PluginFeature.NetworkHttpListen],
                DateTimeOffset.UtcNow));
            Assert.Empty(failingConfig.PluginGrants);
            Assert.Empty(failingConfig.Admissions);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AppConfigRejectsInvalidGrantLevelDuringColdValidation()
    {
        var plugins = new DaemonPluginsConfig { GrantLevel = "garbage" };

        Assert.Throws<ArgumentException>(() =>
            new AppConfig(11452, "secret", "main-token", plugins: plugins));
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

    private static PluginManifest Manifest(
        string id,
        PluginFeature[] features,
        string manifestDigest = "digest")
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
            manifestDigest);
    }

    private sealed class FakePreflightConsole(
        bool isInteractive,
        PluginPreflightDecision decision) : IPluginPreflightConsole
    {
        internal int PromptCount { get; private set; }

        public bool IsInteractive { get; } = isInteractive;

        public PluginPreflightDecision Prompt(PluginPreflightRequest request)
        {
            _ = request;
            PromptCount++;
            return decision;
        }
    }

    private sealed class FakeAdmissionStore(bool succeeds) : IPluginAdmissionStore
    {
        internal int CallCount { get; private set; }

        public bool TryPersistPermanent(
            PluginManifest manifest,
            ImmutableArray<PluginFeature> expandedGrants,
            DateTimeOffset decidedAt)
        {
            _ = manifest;
            _ = expandedGrants;
            _ = decidedAt;
            CallCount++;
            return succeeds;
        }
    }
}
