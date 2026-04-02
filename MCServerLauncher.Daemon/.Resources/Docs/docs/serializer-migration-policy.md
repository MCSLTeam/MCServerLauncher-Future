# Serializer Migration Policy

This document defines the protocol, compatibility, and migration policies for the JSON serialization stack across Daemon, Common, and DaemonClient projects. All guarantees listed here are backed by frozen fixtures and characterization tests in `MCServerLauncher.ProtocolTests`.

---

## 1. Compatibility Model

### 1.1 Coordinated Cutover Assumption

The migration from Newtonsoft.Json to System.Text.Json follows a **coordinated cutover** model:

- Daemon and DaemonClient implementation work proceeds together as a coordinated unit
- Web schema compatibility is preserved through frozen RPC fixtures, not through mixed-version runtime guarantees
- There is no supported scenario where a T14+ Daemon runs against a pre-T14 DaemonClient (or vice versa)

This assumption simplifies the migration by avoiding complex runtime compatibility shims.

### 1.2 RPC Schema-Shape Lock

The following are **locked** (MUST NOT change without explicit plan amendment):

| Aspect | Lock Status | Verification |
|--------|-------------|--------------|
| Envelope field names (`action`, `params`, `id`, `status`, `data`, `message`, `retcode`) | LOCKED | `RpcGolden` tests |
| Event packet field names (`event`, `meta`, `data`, `time`) | LOCKED | `RpcGoldenEvent` tests |
| Enum string casing (snake_case) in wire format | LOCKED | `ConverterParity` tests |
| Nested parameter/result property names | LOCKED | Fixture comparison |
| Envelope structure and nesting | LOCKED | Structural JSON comparison |

### 1.3 Semantic Cleanup vs Schema Lock

Some semantics are explicitly **allowed to change** with documentation:

| Aspect | Status | Notes |
|--------|--------|-------|
| `required` vs `null` policy for envelope fields | CLEANUP ALLOWED | Must be explicitly documented per-field |
| Missing field handling | CLEANUP ALLOWED | Tests distinguish schema-lock from semantic policy |
| Default value behavior | CLEANUP ALLOWED | Where not fixture-covered |

The distinction is enforced in test comments: tests tagged `SchemaLocked` assert only field names and shapes, while tests tagged `SemanticCleanup` may be updated when intentional policy changes occur.

---

## 2. Required/Null Policy for Action/Event Envelopes

### 2.1 ActionRequest

| Field | Presence Rule | Null Rule | Verification |
|-------|---------------|-----------|--------------|
| `action` | Required | Non-null | `RpcGolden` fixture tests |
| `params` | Required | May be null or object | T7 explicit required annotation |
| `id` | Required | Non-null GUID | `RpcGolden` fixture tests |

### 2.2 ActionResponse

| Field | Presence Rule | Null Rule | Verification |
|-------|---------------|-----------|--------------|
| `status` | Required | Non-null enum (snake_case) | `RpcGolden` fixture tests |
| `data` | Required | May be null or value | T7 explicit required annotation |
| `message` | Required | May be null or string | T7 explicit required annotation |
| `retcode` | Required | Non-null int | `RpcGolden` fixture tests |
| `id` | Required | Non-null GUID | `RpcGolden` fixture tests |

### 2.3 EventPacket

| Field | Presence Rule | Null Rule | Verification |
|-------|---------------|-----------|--------------|
| `event` | Required | Non-null enum (snake_case) | `RpcGoldenEvent` tests |
| `meta` | Required | May be null or object | `EventMetaPolicy` tests |
| `data` | Required | May be null or value | `RpcGoldenEvent` tests |
| `time` | Required | Non-null timestamp | `RpcGoldenEvent` tests |

### 2.4 Explicit Null vs Missing

For event payloads, the system distinguishes:

- **Missing property** (C# null): Treated as "not provided" for optional meta
- **Explicit `"meta": null`**: Buffered as `JsonPayloadBuffer` with `ValueKind.Null`, triggers `ArgumentException` for meta-required event types

This behavior is verified in `EventMetaPolicy` characterization tests.

---

## 3. Persistence Migration and Backup Policy

### 3.1 Default Migration Behavior

For daemon-managed JSON files (`config.json`, instance configs, event-rule configs):

1. **Read old**: Deserialize existing JSON if present
2. **Validate**: Confirm content is valid JSON and matches expected contract
3. **Backup valid content**: Copy existing valid JSON to `.bak` before overwrite
4. **Write new**: Serialize and write new content

### 3.2 Backup Semantics

| Scenario | Backup Created | Verification |
|----------|----------------|--------------|
| Valid existing JSON | YES | `BackupBehavior` tests |
| Missing file | NO (nothing to backup) | `BackupBehavior` tests |
| Invalid existing JSON | NO (warning path) | `BackupBehavior` tests |
| Direct write (e.g., `AppConfig.TrySave`) | NO | Explicitly documented per-path |

### 3.3 EventRule Persistence

Event rules are persisted **inside** `daemon_instance.json` under the `event_rules` array, not as separate files. The migration policy applies to the containing file.

### 3.4 Exception Handling

- `FileManager.ReadJson<T>`: Throws on invalid JSON (not normalized)
- `FileManager.ReadJsonOr<T>`: Returns default for missing files, throws on invalid JSON
- `FileManager.WriteJsonAndBackup<T>`: Writes new content even if existing is invalid (backup skipped)

---

## 4. Serializer Ownership Split

### 4.1 Ownership Boundaries

| Boundary | Owner | Location | Responsibility |
|----------|-------|----------|----------------|
| Daemon RPC | Daemon | `DaemonRpcJsonBoundary` | Request/response serialization |
| Daemon Persistence | Daemon | `DaemonPersistenceJsonBoundary` | Config/instance file I/O |
| DaemonClient RPC | DaemonClient | `DaemonClientRpcJsonBoundary` | Request send, response receive |
| Common Foundations | Common | `StjResolver`, converters | Shared type resolution, converters |

### 4.2 Boundary Options Configuration

Each boundary owns its `JsonSerializerOptions` with:

- Snake-case naming policy (`JsonNamingPolicy.SnakeCaseLower`)
- Enum string conversion (`JsonStringEnumConverter` with snake_case)
- Field inclusion for structs
- Boundary-specific converter sets
- Source-generation resolver composition

### 4.3 Source Generation Contexts

Contexts are partitioned by concern to avoid over-coupling:

| Context | Types | Owner |
|---------|-------|-------|
| `RpcEnvelopeContext` | ActionRequest, ActionResponse, EventPacket | Common |
| `ActionParametersContext` | Parameter DTOs | Common |
| `ActionResultsContext` | Result DTOs | Common |
| `EventDataContext` | IEventMeta, IEventData implementations | Common |
| `PersistenceContext` | InstanceConfig, AppConfig (minimal) | Daemon |
| `DaemonRpcSerializerContext` | RPC boundary composition | Daemon |
| `DaemonPersistenceSerializerContext` | Persistence boundary (minimal) | Daemon |
| `DaemonClientRpcSerializerContext` | Client RPC boundary | DaemonClient |

### 4.4 Reflection Fallback Policy

Reflection fallback is controlled via boundary policy enums:

- `TrimFriendlyDefault`: Follows `AppContext.TryGetSwitch("System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault", ...)`
- Defaults to true when unset (Microsoft guidance for trim-friendly defaults)
- Can be explicitly disabled for AOT/trim scenarios

---

## 5. Non-Goals and Deferred Cleanup

### 5.1 Explicitly Out of Scope

The following are **not** covered by this migration plan:

| Item | Reason | Deferred To |
|------|--------|-------------|
| WPF client JSON changes | Scope boundary | Separate plan if needed |
| Web frontend changes | Scope boundary | Web project ownership |
| TaskExecutor/TaskScheduler deep changes | Guardrail protection | Explicit user consultation required |
| Full Newtonsoft removal from Daemon | Blocked by remaining dependencies | Post-T14 (Permission converter, Forge installer) |
| Generic codec pluggability abstraction | Side effect only | Future architecture if needed |
| Blanket Utf8JsonReader rewrite | Performance optimization only | Named hot paths only |

### 5.2 Remaining Newtonsoft Dependencies (T14)

After T14 cleanup, the following still depend on Newtonsoft.Json:

- `Remote/Authentication/Permission.cs`: Custom converter
- `Management/Installer/MinecraftForge/**`: Installer model parsing

These are intentionally deferred to avoid blocking the core migration.

### 5.3 JsonSubTypes Removal

`JsonSubTypes` package reference was removed in T14. EventRule polymorphism now uses explicit `JsonConverter` implementations with discriminator-based deserialization. See `EventRuleDiscriminatorCharacterizationTests` for verified behavior.

The following discriminator values are locked for compatibility:

**Ruleset discriminators:**
- `AlwaysTrue` -> AlwaysTrueRuleset
- `AlwaysFalse` -> AlwaysFalseRuleset  
- `InstanceStatus` -> InstanceStatusRuleset

**Trigger discriminators:**
- `ConsoleOutput` -> ConsoleOutputTrigger
- `Schedule` -> ScheduleTrigger
- `InstanceStatus` -> InstanceStatusTrigger

**Action discriminators:**
- `SendCommand` -> SendCommandAction
- `ChangeInstanceStatus` -> ChangeInstanceStatusAction
- `SendNotification` -> SendNotificationAction

---

## 6. Verified Guarantees Reference

### 6.1 Test Categories

| Category | Description | Fixture Location |
|----------|-------------|------------------|
| `RpcGolden` | RPC wire format compatibility | `Fixtures/Rpc/**` |
| `RpcGoldenAction` | Action request/response fixtures | `Fixtures/Rpc/ActionRequest/**`, `Fixtures/Rpc/ActionResponse/**` |
| `RpcGoldenEvent` | Event packet fixtures | `Fixtures/Rpc/EventPacket/**` |
| `ConverterParity` | Converter behavior parity | `Fixtures/ConverterParity/**` |
| `PersistenceGolden` | Persistence round-trip | `Fixtures/Persistence/**` |
| `BackupBehavior` | Backup semantics | Asserted in-memory via temp files |
| `EventRuleKnown` | Known discriminator handling | `Fixtures/Persistence/EventRule/**` |
| `EventRuleUnknown` | Unknown discriminator errors | `Fixtures/Persistence/EventRule/**` |
| `EventMetaPolicy` | Null vs missing meta policy | Asserted via helper tests |
| `DaemonInbound` | Daemon inbound parsing | Asserted via adapter tests |
| `DaemonOutbound` | Daemon outbound serialization | Asserted via fixture comparison |
| `DaemonClientInbound` | Client inbound handling | T11 characterization |
| `DaemonClientOutbound` | Client outbound sending | T12 characterization |

### 6.2 Fixture Files (24 Total)

**RPC Fixtures (7):**
- `ActionRequest/ping-empty-params.json`
- `ActionRequest/subscribe-event-null-meta.json`
- `ActionRequest/subscribe-event-concrete-meta.json`
- `ActionRequest/save-event-rules-nested-parameter.json`
- `ActionResponse/success-empty-object-data.json`
- `ActionResponse/success-typed-data.json`
- `ActionResponse/error-null-data-message-retcode-shape.json`
- `EventPacket/null-meta-structured-data.json`
- `EventPacket/with-meta-and-data.json`

**Converter Parity Fixtures (6):**
- `Guid/valid-string-roundtrip.json`
- `Guid/invalid-string-deserialize.json`
- `Encoding/valid-web-name.json`
- `Encoding/invalid-name-exception.json`
- `PlaceHolderString/null-empty-non-empty.json`
- `Enum/snake-case-formatting.json`
- `Enum/required-null-semantics.json`
- `Permission/valid-invalid-behavior.json`

**Persistence Fixtures (7):**
- `Config/valid-config.json`
- `InstanceConfig/representative-daemon-instance.json`
- `InstanceConfig/event-rule-heavy-daemon-instance.json`
- `EventRule/known-discriminators-event-rule.json`
- `EventRule/unknown-trigger-discriminator-event-rule.json`
- `EventRule/missing-ruleset-discriminator-event-rule.json`
- `EventRule/invalid-action-discriminator-event-rule.json`

---

## 7. Policy Change Process

To modify any policy in this document:

1. **Identify affected fixtures**: Determine which frozen fixtures would change
2. **Update or add tests**: Ensure the new behavior has test coverage
3. **Document in decisions**: Add entry to `.sisyphus/notepads/json-stj-migration-daemon/decisions.md`
4. **Update validation**: If anchor references change, update `DocumentationValidationTests`
5. **Append to learnings**: Document any behavioral discoveries

### 7.1 Allowed Without Plan Amendment

- Adding new test categories for new contracts
- Adding new fixtures for new message types
- Documentation clarifications that do not change guarantees

### 7.2 Requires Plan Amendment

- Changing locked schema aspects (field names, envelope structure)
- Changing backup semantics
- Changing required/null policy for existing fields
- Removing Newtonsoft dependencies listed in 5.2

---

## 8. Validation

This document is validated by `DocumentationValidationTests` which asserts:

1. All anchor references in this document point to existing test categories
2. All fixture path references match actual files on disk
3. Policy counts match discovered test categories

Run validation:
```bash
dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj --filter DocumentationValidation
```

---

*Last updated: Post-T14 cleanup*
*Policy version: 1.0*
*Verified by: ProtocolTests characterization suite*
