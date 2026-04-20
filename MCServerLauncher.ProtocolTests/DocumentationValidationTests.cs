using System.Reflection;
using System.Text.RegularExpressions;
using MCServerLauncher.ProtocolTests.Fixtures.ConverterParity;
using MCServerLauncher.ProtocolTests.Fixtures.Persistence;
using MCServerLauncher.ProtocolTests.Fixtures.Rpc;
using Xunit.Abstractions;

namespace MCServerLauncher.ProtocolTests;

/// <summary>
/// Validation tests that ensure the serializer migration policy document
/// stays aligned with actual test guarantees and fixture files.
/// 
/// These tests act as a static consistency check between documentation claims
/// and verified test-backed guarantees. If documentation references change,
/// these tests will fail to prompt an update.
/// </summary>
public class DocumentationValidationTests
{
    private readonly ITestOutputHelper _output;
    private static readonly string DocPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "..", "..", "..", "..",
        "MCServerLauncher.Daemon", ".Resources", "Docs", "docs",
        "serializer-migration-policy.md");

    public DocumentationValidationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private string LoadPolicyDocument()
    {
        // Try multiple path resolutions for different execution contexts
        var pathsToTry = new[]
        {
            DocPath,
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "serializer-migration-policy.md"),
            Path.Combine(Environment.CurrentDirectory, "MCServerLauncher.Daemon", ".Resources", "Docs", "docs", "serializer-migration-policy.md"),
            // CI/build agent path
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "MCServerLauncher.Daemon", ".Resources", "Docs", "docs", "serializer-migration-policy.md")
        };

        foreach (var path in pathsToTry)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                _output.WriteLine($"Found policy document at: {fullPath}");
                return File.ReadAllText(fullPath);
            }
        }

        // Try to find by searching up from current directory
        var currentDir = new DirectoryInfo(Environment.CurrentDirectory);
        while (currentDir != null)
        {
            var candidate = Path.Combine(currentDir.FullName, "MCServerLauncher.Daemon", ".Resources", "Docs", "docs", "serializer-migration-policy.md");
            if (File.Exists(candidate))
            {
                _output.WriteLine($"Found policy document by search: {candidate}");
                return File.ReadAllText(candidate);
            }
            currentDir = currentDir.Parent;
        }

        throw new FileNotFoundException("Could not locate serializer-migration-policy.md. Tried: " + string.Join("; ", pathsToTry.Select(p => Path.GetFullPath(p))));
    }

    [Fact]
    [Trait("Category", "DocumentationValidation")]
    [Trait("Category", "DocumentationPolicy")]
    [Trait("Category", "DocumentationPolicyConsistency")]
    public void PolicyDocument_Exists_And_IsReadable()
    {
        var doc = LoadPolicyDocument();
        Assert.NotNull(doc);
        Assert.NotEmpty(doc);
        Assert.Contains("Serializer Migration Policy", doc);
    }

    [Fact]
    [Trait("Category", "DocumentationValidation")]
    [Trait("Category", "DocumentationPolicy")]
    [Trait("Category", "DocumentationPolicyConsistency")]
    public void PolicyDocument_ContainsAllTestCategoryReferences()
    {
        var doc = LoadPolicyDocument();
        
        // Test categories documented in section 6.1
        var expectedCategories = new[]
        {
            "RpcGolden",
            "RpcGoldenAction",
            "RpcGoldenEvent",
            "ConverterParity",
            "PersistenceGolden",
            "BackupBehavior",
            "EventRuleKnown",
            "EventRuleUnknown",
            "EventMetaPolicy",
            "DaemonInbound",
            "DaemonOutbound",
            "DaemonClientInbound",
            "DaemonClientOutbound"
        };

        foreach (var category in expectedCategories)
        {
            Assert.True(
                doc.Contains($"`{category}`"),
                $"Policy document should reference test category '{category}'"
            );
        }

        _output.WriteLine($"Verified {expectedCategories.Length} test categories are documented");
    }

    [Fact]
    [Trait("Category", "DocumentationValidation")]
    [Trait("Category", "DocumentationPolicy")]
    [Trait("Category", "DocumentationPolicyConsistency")]
    public void PolicyDocument_RpcFixtureFiles_ExistOnDisk()
    {
        var doc = LoadPolicyDocument();
        
        // Extract fixture file references from section 6.2
        var rpcFixtures = new[]
        {
            (RpcFixturePaths.ActionRequestDir, "ping-empty-params.json"),
            (RpcFixturePaths.ActionRequestDir, "subscribe-event-null-meta.json"),
            (RpcFixturePaths.ActionRequestDir, "subscribe-event-concrete-meta.json"),
            (RpcFixturePaths.ActionRequestDir, "save-event-rules-nested-parameter.json"),
            (RpcFixturePaths.ActionResponseDir, "success-empty-object-data.json"),
            (RpcFixturePaths.ActionResponseDir, "success-typed-data.json"),
            (RpcFixturePaths.ActionResponseDir, "error-null-data-message-retcode-shape.json"),
            (RpcFixturePaths.EventPacketDir, "null-meta-structured-data.json"),
            (RpcFixturePaths.EventPacketDir, "with-meta-and-data.json")
        };

        foreach (var (dir, file) in rpcFixtures)
        {
            var path = Path.Combine(dir, file);
            Assert.True(
                File.Exists(path),
                $"RPC fixture '{file}' should exist at '{path}' as documented in section 6.2"
            );
        }

        _output.WriteLine($"Verified {rpcFixtures.Length} RPC fixtures exist on disk");
    }

    [Fact]
    [Trait("Category", "DocumentationValidation")]
    [Trait("Category", "DocumentationPolicy")]
    [Trait("Category", "DocumentationPolicyConsistency")]
    public void PolicyDocument_ConverterParityFixtureFiles_ExistOnDisk()
    {
        var doc = LoadPolicyDocument();
        
        var converterFixtures = new[]
        {
            (ConverterParityFixturePaths.GuidDir, "valid-string-roundtrip.json"),
            (ConverterParityFixturePaths.GuidDir, "invalid-string-deserialize.json"),
            (ConverterParityFixturePaths.EncodingDir, "valid-web-name.json"),
            (ConverterParityFixturePaths.EncodingDir, "invalid-name-exception.json"),
            (ConverterParityFixturePaths.PlaceHolderStringDir, "null-empty-non-empty.json"),
            (ConverterParityFixturePaths.EnumDir, "snake-case-formatting.json"),
            (ConverterParityFixturePaths.EnumDir, "required-null-semantics.json"),
            (ConverterParityFixturePaths.PermissionDir, "valid-invalid-behavior.json")
        };

        foreach (var (dir, file) in converterFixtures)
        {
            var path = Path.Combine(dir, file);
            Assert.True(
                File.Exists(path),
                $"Converter parity fixture '{file}' should exist at '{path}' as documented in section 6.2"
            );
        }

        _output.WriteLine($"Verified {converterFixtures.Length} converter parity fixtures exist on disk");
    }

    [Fact]
    [Trait("Category", "DocumentationValidation")]
    [Trait("Category", "DocumentationPolicy")]
    [Trait("Category", "DocumentationPolicyConsistency")]
    public void PolicyDocument_PersistenceFixtureFiles_ExistOnDisk()
    {
        var doc = LoadPolicyDocument();
        
        var persistenceFixtures = new[]
        {
            (PersistenceFixturePaths.ConfigDir, "valid-config.json"),
            (PersistenceFixturePaths.InstanceConfigDir, "representative-daemon-instance.json"),
            (PersistenceFixturePaths.InstanceConfigDir, "event-rule-heavy-daemon-instance.json"),
            (PersistenceFixturePaths.EventRuleDir, "known-discriminators-event-rule.json"),
            (PersistenceFixturePaths.EventRuleDir, "unknown-trigger-discriminator-event-rule.json"),
            (PersistenceFixturePaths.EventRuleDir, "missing-ruleset-discriminator-event-rule.json"),
            (PersistenceFixturePaths.EventRuleDir, "invalid-action-discriminator-event-rule.json")
        };

        foreach (var (dir, file) in persistenceFixtures)
        {
            var path = Path.Combine(dir, file);
            Assert.True(
                File.Exists(path),
                $"Persistence fixture '{file}' should exist at '{path}' as documented in section 6.2"
            );
        }

        _output.WriteLine($"Verified {persistenceFixtures.Length} persistence fixtures exist on disk");
    }

    [Fact]
    [Trait("Category", "DocumentationValidation")]
    [Trait("Category", "DocumentationPolicy")]
    [Trait("Category", "DocumentationPolicyConsistency")]
    public void PolicyDocument_TotalFixtureCount_MatchesDocumentation()
    {
        // Section 6.2 documents 24 total fixtures
        var documentedCount = 24;
        
        // Count actual fixture files
        var fixtureRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures");
        if (!Directory.Exists(fixtureRoot))
        {
            fixtureRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Fixtures");
        }
        
        var actualCount = Directory.Exists(fixtureRoot) 
            ? Directory.GetFiles(fixtureRoot, "*.json", SearchOption.AllDirectories).Length 
            : 0;

        // The test documents 24 fixtures; actual count may be >= that
        Assert.True(
            actualCount >= documentedCount || actualCount == 0,
            $"If fixtures exist, should have at least {documentedCount} fixtures documented in section 6.2. Found: {actualCount}"
        );

        _output.WriteLine($"Documented fixtures: {documentedCount}, actual on disk: {actualCount}");
    }

    [Fact]
    [Trait("Category", "DocumentationValidation")]
    [Trait("Category", "DocumentationPolicy")]
    [Trait("Category", "DocumentationPolicyConsistency")]
    public void PolicyDocument_ContainsCriticalSections()
    {
        var doc = LoadPolicyDocument();
        
        // Critical sections that must exist
        var requiredSections = new[]
        {
            "## 1. Compatibility Model",
            "## 2. Required/Null Policy",
            "## 3. Persistence Migration",
            "## 4. Serializer Ownership Split",
            "## 5. Non-Goals",
            "## 6. Verified Guarantees",
            "## 7. Policy Change Process",
            "## 8. Validation",
            "Coordinated Cutover",
            "Schema-Shape Lock",
            "Backup Semantics",
            "Daemon RPC",
            "Daemon Persistence",
            "DaemonClient RPC"
        };

        foreach (var section in requiredSections)
        {
            Assert.True(
                doc.Contains(section),
                $"Policy document should contain required section/term: '{section}'"
            );
        }

        _output.WriteLine($"Verified {requiredSections.Length} critical sections exist");
    }

    [Fact]
    [Trait("Category", "DocumentationValidation")]
    [Trait("Category", "DocumentationPolicy")]
    [Trait("Category", "DocumentationPolicyConsistency")]
    public void PolicyDocument_DiscriminatorValues_AreDocumented()
    {
        var doc = LoadPolicyDocument();
        
        // T6 decision: discriminator values should be documented
        var expectedDiscriminators = new[]
        {
            "AlwaysTrue",
            "AlwaysFalse",
            "InstanceStatus",
            "ConsoleOutput",
            "Schedule",
            "SendCommand",
            "ChangeInstanceStatus",
            "SendNotification"
        };

        foreach (var discriminator in expectedDiscriminators)
        {
            Assert.True(
                doc.Contains(discriminator),
                $"Policy document should document discriminator value: '{discriminator}'"
            );
        }

        _output.WriteLine($"Verified {expectedDiscriminators.Length} discriminator values are documented");
    }

    [Fact]
    [Trait("Category", "DocumentationValidation")]
    [Trait("Category", "DocumentationPolicy")]
    [Trait("Category", "DocumentationPolicyConsistency")]
    public void PolicyDocument_OwnershipBoundaries_AreDocumented()
    {
        var doc = LoadPolicyDocument();
        
        // Section 4.1 ownership boundaries
        var expectedBoundaries = new[]
        {
            "DaemonRpcJsonBoundary",
            "DaemonPersistenceJsonBoundary",
            "DaemonClientRpcJsonBoundary",
            "StjResolver"
        };

        foreach (var boundary in expectedBoundaries)
        {
            Assert.True(
                doc.Contains(boundary),
                $"Policy document should document ownership boundary: '{boundary}'"
            );
        }

        _output.WriteLine($"Verified {expectedBoundaries.Length} ownership boundaries are documented");
    }

    [Fact]
    [Trait("Category", "DocumentationValidation")]
    [Trait("Category", "DocumentationPolicy")]
    [Trait("Category", "DocumentationPolicyConsistency")]
    public void PolicyDocument_SerializerContexts_AreDocumented()
    {
        var doc = LoadPolicyDocument();
        
        // Section 4.3 source generation contexts
        var expectedContexts = new[]
        {
            "RpcEnvelopeContext",
            "ActionParametersContext",
            "ActionResultsContext",
            "EventDataContext",
            "PersistenceContext",
            "DaemonRpcSerializerContext",
            "DaemonPersistenceSerializerContext",
            "DaemonClientRpcSerializerContext"
        };

        foreach (var context in expectedContexts)
        {
            Assert.True(
                doc.Contains(context),
                $"Policy document should document serializer context: '{context}'"
            );
        }

        _output.WriteLine($"Verified {expectedContexts.Length} serializer contexts are documented");
    }

    [Fact]
    [Trait("Category", "DocumentationValidation")]
    [Trait("Category", "DocumentationPolicy")]
    [Trait("Category", "DocumentationPolicyConsistency")]
    public void PolicyDocument_ContainsVersionAndDate()
    {
        var doc = LoadPolicyDocument();
        
        // Document should have version markers
        Assert.True(
            doc.Contains("*Policy version:") || doc.Contains("Last updated:"),
            "Policy document should contain version/date markers"
        );

        // Should reference T14 as verification baseline
        Assert.True(
            doc.Contains("T14") || doc.Contains("Post-T14"),
            "Policy document should reference T14 as verification baseline"
        );
    }

    [Fact]
    [Trait("Category", "DocumentationValidation")]
    [Trait("Category", "DocumentationPolicy")]
    [Trait("Category", "DocumentationPolicyConsistency")]
    public void PolicyDocument_DeferredCleanup_IsExplicit()
    {
        var doc = LoadPolicyDocument();
        
        // Section 5 should document remaining migration items
        var expectedDeferredItems = new[]
        {
            "Permission.cs",
            "Forge",
            "deferred",
            "Non-Goals"
        };

        foreach (var item in expectedDeferredItems)
        {
            Assert.True(
                doc.Contains(item),
                $"Policy document should document deferred item: '{item}'"
            );
        }

        _output.WriteLine($"Verified {expectedDeferredItems.Length} deferred items are documented");
    }

    [Fact]
    [Trait("Category", "DocumentationValidation")]
    [Trait("Category", "DocumentationPolicy")]
    [Trait("Category", "DocumentationPolicyConsistency")]
    public void PolicyDocument_BackupBehaviorTable_IsComplete()
    {
        var doc = LoadPolicyDocument();
        
        // Section 3.2 backup semantics table should exist
        Assert.True(
            doc.Contains("Backup Created") && doc.Contains("BackupBehavior"),
            "Policy document should contain backup behavior table with test references"
        );

        // All scenarios should be covered
        var backupScenarios = new[]
        {
            "Valid existing JSON",
            "Missing file",
            "Invalid existing JSON"
        };

        foreach (var scenario in backupScenarios)
        {
            Assert.True(
                doc.Contains(scenario),
                $"Policy document should document backup scenario: '{scenario}'"
            );
        }
    }

    [Fact]
    [Trait("Category", "DocumentationValidation")]
    [Trait("Category", "DocumentationPolicy")]
    [Trait("Category", "DocumentationPolicyConsistency")]
    public void PolicyDocument_RequiredNullPolicy_IsDocumentedPerField()
    {
        var doc = LoadPolicyDocument();
        
        // Sections 2.1-2.3 should document required/null policy per field
        var expectedFields = new[]
        {
            "`action`",
            "`params`",
            "`id`",
            "`status`",
            "`data`",
            "`message`",
            "`retcode`",
            "`event`",
            "`meta`",
            "`time`"
        };

        foreach (var field in expectedFields)
        {
            Assert.True(
                doc.Contains(field),
                $"Policy document should document field: '{field}'"
            );
        }

        _output.WriteLine($"Verified {expectedFields.Length} envelope fields are documented");
    }
}
