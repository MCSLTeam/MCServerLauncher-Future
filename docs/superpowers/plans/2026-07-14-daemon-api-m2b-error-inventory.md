# Phase 4C M2b Legacy Error Inventory

> **Authority:** Companion inventory for `2026-06-27-daemon-api-inprocess-plugin-v2-plan.md`.
>
> **Scope:** M2b replaces daemon-internal `Result<T, Error>` paths with the frozen `Result<T, DaemonError>` model. It does not delete the V1 action adapter; that remains an M4 deletion target.

## 1. Baseline

The current branch still has direct `Result<..., Error>` signatures in 15 daemon implementation files and two protocol-test fixtures. Three additional daemon files provide the legacy error helper or subtype. The residuals are concentrated in instance management, factory/installer composition, and the legacy result helpers:

| Area | Files |
|---|---|
| Error helpers | `src/MCServerLauncher.Daemon/Utils/Error.cs`, `ErrorExtensions.cs`, `ResultExt.cs`; `Management/InstanceTargetPathValidator.cs` derives from `Error` |
| Instance manager and configuration | `Management/IInstanceManager.cs`, `InstanceManager.cs`, `InstanceUpdateCoordinator.cs`, `InstanceConfigExtensions.cs`, `InstanceFactoryConfigurationExtensions.cs`; `Application/LocalInstanceApplication.cs` still pattern-matches `InstancePathValidationError` |
| Factories and registry | `Management/Factory/IInstanceFactory.cs`, `InstanceFactoryRegistry.cs`, `MCUniversalFactory.cs`, `MCForgeFactory.cs` |
| Installers | `Management/Installer/IInstanceInstaller.cs`, `PassthroughInstaller.cs`, `MinecraftForge/ForgeInstallerBase.cs`, `ForgeInstallerV1.cs`, `ForgeInstallerV2.cs` |
| Test fixtures | `tests/MCServerLauncher.ProtocolTests/Management/InstanceManagerCreateTransactionTests.cs`, `Registry/InstanceFactoryRegistryStaticRegistrationTests.cs` |

The inventory command for this baseline is:

```text
rg -l "Result<[^\\r\\n>]+,\\s*Error>" src tests benchmarks tools --glob '*.cs'
```

## 2. M2b Conversion Targets

### Error model and helpers

- Replace `Utils.Error`, `ErrorExtensions`, and `ResultExt` overloads that manufacture `Error` values.
- Preserve exception causes for daemon logs, but expose only a typed `DaemonError` code/kind/details value at an application boundary.
- Remove implicit `string`/`Exception` to `Error` conversions from migrated code. They hide error classification and make accidental legacy results easy to reintroduce.

### Instance manager and settings

- Convert `IInstanceManager.TryAddInstance`, `GetInstanceSettings`, and `UpdateInstanceSettings` to `Result<..., DaemonError>`.
- Convert `InstanceManager`, `InstanceUpdateCoordinator`, and both configuration extension files to construct the appropriate validation, not-found, conflict, storage, or internal `DaemonError` subtype.
- Remove the `LocalInstanceApplication` dependency on the legacy `InstancePathValidationError` subtype; classify the typed manager result directly.
- Keep the existing transaction and cancellation behavior. M2b changes the error value, not lifecycle semantics or persistence ordering.
- Convert `InstanceTargetPathValidator` from its `Error` subclass to a typed validation error or an internal validation helper that returns `DaemonError`.

### Factories and installers

- Change `IInstanceFactory`, `InstanceFactoryRegistry`, `MCUniversalFactory`, and `MCForgeFactory` delegates/results to `DaemonError`.
- Change `IInstanceInstaller`, `PassthroughInstaller`, and Forge installer base/V1/V2 results to `DaemonError`.
- Map download, archive, EULA, profile, and installer failures to stable error codes; retain detailed exception text in logs rather than serialized details.

### Tests

- Update the two registry/manager fixtures to use `DaemonError` and assert error kind/code where behavior is intentionally covered.
- Rebuild the manager/application test seam after the signature change. The main indirect compile/test consumers are `Application/LocalInstanceApplicationTests.cs`, `Application/LocalEventRuleApplicationTests.cs`, `Management/InstanceManagerCreateTransactionTests.cs`, `Rpc/InstanceSettingsCoordinatorTests.cs`, and the instance process-event tests.
- Add focused mapping assertions for validation, not-found, conflict, storage, and unexpected exception paths before deleting the legacy helper types.

## 3. Explicit Exclusions

The following are not M2b conversion targets:

- `src/MCServerLauncher.Daemon/Remote/Action/ActionError.cs` and V1 `IActionHandler`/response code. They are wire-adapter code retained until the M4 V1 deletion gate.
- V1/Common/client/generator source that is still required by the release-atomic deletion manifest.
- Log messages containing the word `Error`, `args.Error`, or JSON-RPC error construction. These are not `Utils.Error` result values and must be audited separately only when their type boundary changes.

## 4. Closure Gates

M2b is closed only when:

1. No non-V1 daemon implementation path returns or constructs `Utils.Error`.
2. `IInstanceManager`, factory, and installer signatures use `Result<..., DaemonError>` throughout their migrated call graph.
3. Focused manager/factory/installer tests and the full Release protocol suite pass.
4. The residual search is allowlisted to V1 adapter files only; no broad `rg` success is treated as proof.
5. `git diff --check` passes and the parent execution plan records the verification evidence.

## Changelog

- 2026-07-14: Captured the M2b legacy `Error` residual inventory, conversion ownership, V1 exclusions, and closure search gates after M2a completion.
- 2026-07-15: Closed M2b after isolated replacement-installer transaction hardening and independent Sol max review with `P0=0 / P1=0 / P2=0`. Expanded focused M2b/application tests passed `133/133`; Release ProtocolTests and the required `--no-build` repeat each passed `1196/1196`; daemon/full-solution Release builds passed at `0 warnings / 0 errors`; WPF.Tests passed `20/20`; ProtocolDocs `--check` passed at hash `3e100b801b7c34c4e6ac61c36b5d7f064b9c1936d81356aa30695f4894146b86`; the exact residual search returned only the two allowlisted V1 adapter calls; and `git diff --check` passed. Security regressions cover reparse-point traversal without external-file mutation and fail-closed Windows case aliases without live-storage mutation.
