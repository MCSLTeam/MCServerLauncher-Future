# Instance Management UI Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve instance settings validation, instance card resource visibility, lifecycle action confirmations, and move HomePage debug-only controls into DebugPage.

**Architecture:** Keep behavior inside WPF presentation/view-model boundaries. Reuse create-instance validation helpers for settings save, consume existing `InstanceReport.PerformanceCounter` data for cards, keep daemon lifecycle authority unchanged, and use source-generated `System.Text.Json` metadata for Common ping payloads.

**Tech Stack:** C# 14, WPF, CommunityToolkit.Mvvm, iNKORE.UI.WPF.Modern, `.resx` i18n, daemon client RPC.

---

## Execution Note

Subagent-driven execution is preferred by `RULES.md`, but the available multi-agent tool can only be used when the user explicitly asks for sub-agents. This task is executed inline against this plan.

## Touched Areas

- `frontend`: WPF pages, view models, notifications, action confirmations.
- `backend`: event trigger ruleset evaluation logging.
- `protocol`: read existing `InstanceReport.PerformanceCounter` data only; no wire contract change.
- `serialization`: SLP ping payload source-generated metadata.
- `tests`: build and protocol-test verification.
- `docs`: this plan and changelog.

## Files

- Modify `MCServerLauncher.WPF/View/Pages/HomePage.xaml`: remove debug controls from the user-facing home page.
- Modify `MCServerLauncher.WPF/View/Pages/HomePage.xaml.cs`: remove debug-only handlers.
- Modify `MCServerLauncher.WPF/View/Pages/DebugPage.xaml`: add console, exception, notification, and file-editor debug controls.
- Modify `MCServerLauncher.WPF/View/Pages/DebugPage.xaml.cs`: host moved debug handlers.
- Modify `MCServerLauncher.WPF/InstanceConsole/ViewModels/InstanceSettingsViewModel.cs`: validate save inputs before upload/RPC.
- Modify `MCServerLauncher.WPF/View/CreateInstanceProvider/CreateInstanceValidation.cs`: make shared validation accessible to instance settings.
- Modify `MCServerLauncher.WPF/ViewModels/Models/InstanceCardModel.cs`: add formatted CPU/memory display and lifecycle capability properties.
- Modify `MCServerLauncher.WPF/InstanceConsole/View/Components/InstancePerformance.xaml.cs`: clamp CPU and memory values before updating text/progress controls.
- Modify `MCServerLauncher.WPF/App.xaml.cs`: guard unhandled exception dialogs against recursive failures.
- Modify `MCServerLauncher.WPF/ExceptionDialog/Window.xaml`: remove modern window chrome/backdrop from the crash dialog and constrain stack text layout.
- Modify `MCServerLauncher.WPF/ExceptionDialog/Window.xaml.cs`: keep feedback launch shell-safe.
- Modify `MCServerLauncher.Common/ProtoType/Instance/InstancePerformanceCounter.cs`: normalize performance counter values at the shared contract boundary.
- Modify `MCServerLauncher.WPF/View/Pages/InstanceManagerPage.xaml`: render compact CPU/memory rows and bind action menu availability.
- Modify `MCServerLauncher.WPF/ViewModels/InstanceManagerViewModel.cs`: add command-layer state guards and confirmations.
- Modify `MCServerLauncher.WPF/Translations/Lang.*.resx`: add any missing user-facing strings.
- Modify `MCServerLauncher.Common/Network/SlpClient.cs`: deserialize ping payloads through source-generated STJ metadata.
- Modify `MCServerLauncher.Daemon/Remote/Event/EventTriggerService.cs`: make non-matching rulesets a normal skip log instead of a failure.
- Modify `MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj`: keep daemon reflection-based STJ fallback enabled for compatibility.
- Modify `MCServerLauncher.ProtocolTests/StjFoundationTests.cs`: verify SLP payload deserialization with reflection disabled.
- Modify `MCServerLauncher.ProtocolTests/ProjectConfigurationTests.cs`: lock daemon JSON reflection fallback project configuration.

## Tasks

### Task 1: Move Debug Controls

- [x] Inspect HomePage and DebugPage controls and code-behind.
- [x] Move debug buttons from `HomePage.xaml` to `DebugPage.xaml`.
- [x] Move handlers from `HomePage.xaml.cs` to `DebugPage.xaml.cs`.
- [x] Ensure HomePage no longer references moved handlers.
- [x] Build WPF.

### Task 2: Instance Settings Save Validation

- [x] Keep `CreateInstanceValidation` internal because instance settings is in the same assembly.
- [x] Add `ValidateBeforeSave()` to `InstanceSettingsViewModel`.
- [x] Validate `Settings.Name` with `TryValidateInstanceName`.
- [x] Validate `Settings.JavaPath` with `TryValidateJavaPath`.
- [x] Validate non-empty replacement core path with `TryValidateLocalJarPath`.
- [x] Push validation failures through injected `INotificationService`.
- [x] Run WPF build.

### Task 3: Instance Card Resource Display

- [x] Add formatted `CpuUsageText`, `MemoryUsageText`, and clamped CPU value properties to `InstanceCardModel`.
- [x] Raise dependent property notifications when raw CPU or memory changes.
- [x] Add compact CPU and memory rows to `InstanceManagerPage.xaml`, matching daemon card density.
- [x] Keep full text visible and tooltiped.
- [x] Run WPF build.

### Task 4: Instance Action Guards And Confirmations

- [x] Add lifecycle capability properties to `InstanceCardModel`.
- [x] Bind menu item `IsEnabled` for normal availability where appropriate.
- [x] Keep command guards authoritative and show warning notification for invalid state.
- [x] Start: require stopped/crashed-like idle state and show confirmation.
- [x] Stop/restart: require running and show confirmation.
- [x] Kill: require running and use countdown confirmation.
- [x] Delete: require stopped, show non-countdown delete confirmation.
- [x] Batch delete: skip non-stopped instances and report failures.
- [x] Run WPF build.

### Task 5: Verification

- [x] Run `dotnet build MCServerLauncher.WPF/MCServerLauncher.WPF.csproj -c Release /m:1`.
- [x] Run `git diff --check`.
- [ ] Before commit, run `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --no-build`.
- [ ] If `--no-build` fails because Release artifacts are stale/missing, run `dotnet build MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release /m:1`, then rerun the exact test command.
- [ ] Confirm `MCServerLauncher.WPF/Modules/Language.cs` remains unstaged unless the user explicitly asks.

### Task 6: Instance Card Regression Fixes

- [x] Replace custom resource bars in `InstanceManagerPage.xaml` with daemon-card-style `ui:ProgressBar` rows.
- [x] Keep fixed card padding and column widths so CPU/memory text remains visible without stretching card height.
- [x] Fix `MenuFlyout` command bindings by binding commands through card-owned command properties and passing the card as the command parameter.
- [x] Remove obsolete manual bar-width properties from `InstanceCardModel`.
- [x] Run WPF Release build.

### Task 7: Event Ruleset Evaluation Logging

- [x] Change a false ruleset match from "evaluation failed" to a debug-level skip message.
- [x] Keep unsupported ruleset types from silently passing.
- [x] Run daemon Release build.

### Task 8: SLP Source-Generated JSON

- [x] Add a failing protocol test showing SLP ping JSON deserializes with reflection fallback disabled.
- [x] Add source-generated STJ context for `PingPayload`.
- [x] Switch `SlpClient.GetSlpAsync` to source-gen deserialize.
- [x] Run the targeted protocol test.

### Task 9: Daemon JSON Reflection Fallback

- [x] Add a failing project-configuration test that requires `MCServerLauncher.Daemon.csproj` to keep `JsonSerializerIsReflectionEnabledByDefault` enabled.
- [x] Change the daemon project property to `true`.
- [x] Run the targeted configuration test.
- [x] Run daemon Release build and full protocol tests.

### Task 10: Publish And Progress Bar Isolation

- [x] Run daemon publish test with `dotnet publish MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj -c Release -r win-x64 --self-contained /m:1`.
- [x] Temporarily remove instance card progress bars from `InstanceManagerPage.xaml` for stack overflow isolation.
- [x] Keep CPU and memory text visible using the existing formatted text bindings.
- [x] Run WPF Release build.

### Task 11: Instance Performance Counter Normalization

- [x] Add tests for `InstancePerformanceCounter` negative, zero, NaN, and infinity values.
- [x] Normalize CPU to `0..100` and memory to `>= 0` in the shared performance counter constructor.
- [x] Keep WPF instance card CPU/memory display resilient when values are zero or malformed.
- [x] Clamp InstanceConsole performance component progress values before assigning to progress bars.
- [x] Run targeted tests, WPF Release build, daemon Release build, and protocol tests.

### Task 12: Exception Dialog Stack Overflow Recursion

- [x] Inspect `so.txt` and identify whether the recursive stack enters `ExceptionDialog.Window`.
- [x] Add a reentrancy guard around WPF unhandled exception dialog display.
- [x] Simplify `ExceptionDialog.Window` by removing iNKORE modern window chrome/backdrop.
- [x] Disable wrap-with-overflow on the large stack trace text box and enable scrollbars.
- [x] Run WPF Release build and protocol tests.

### Task 13: Temporary Instance Resource Isolation

- [x] Comment out instance-manager CPU and memory fetch/update code while isolating the remaining XAML stack overflow.
- [x] Keep instance status/name/type/version updates working.
- [x] Run WPF Release build.

### Task 14: Instance More Menu Binding Isolation

- [x] Remove `x:Reference InstanceMoreButton` bindings from the instance card more menu.
- [x] Bind menu commands directly to command properties on `InstanceCardModel`.
- [x] Keep command parameters bound to the card data context.
- [x] Run WPF Release build.

### Task 15: Restore Instance Resource Text

- [x] Restore instance CPU and memory data population after confirming the more-menu binding was the stack overflow trigger.
- [x] Restore compact CPU and memory text rows without progress bars.
- [x] Keep `x:Reference InstanceMoreButton` out of the more menu.
- [x] Run WPF Release build.

### Task 16: Localized Instance Status And Resource Progress Bars

- [x] Add localized `StatusText` mapping to `InstanceCardModel` using existing `Starting`, `Running`, `Stopping`, `Stopped`, and `Crashed` resource keys.
- [x] Raise `StatusText` change notifications when `Status` changes.
- [x] Change the instance status badge text binding from raw enum value to `StatusText`.
- [x] Restore daemon-card-style `ui:ProgressBar` controls for CPU and memory rows without reintroducing `x:Reference` menu bindings.
- [x] Run WPF Release build and `git diff --check`.

### Task 17: Fix Instance Card Progress Bar Binding Mode

- [x] Correct the XAML parse failure by binding progress bar values to read-only computed properties with `Mode=OneWay`.
- [x] Keep localized status text and CPU/memory formatted text visible alongside progress bars.
- [x] Run WPF Release build and `git diff --check`.

### Task 18: Active Instance Status Guards

- [x] Treat `Starting` and `Running` as active states in instance card state helpers.
- [x] Keep `Starting`, `Running`, and `Stopping` from being startable or deletable.
- [x] Allow stop and kill actions for `Starting` and `Running` so the UI can interrupt a starting process.
- [x] Skip invalid statuses in batch start and batch stop instead of sending daemon calls blindly.
- [x] Run WPF Release build and `git diff --check`.

### Task 19: Responsive Manager Card Widths

- [x] Add a shared WPF card-width converter that divides available grid width across cards using minimum width as the wrap threshold.
- [x] Apply the converter to daemon manager cards.
- [x] Apply the converter to instance manager cards.
- [x] Run WPF Release build, `git diff --check`, and protocol tests before commit.

## Changelog

- Moved HomePage debug-only controls and handlers into DebugPage.
- Added instance settings save validation using existing create-instance validation helpers.
- Added CPU and memory usage display to instance cards using existing `InstanceReport.PerformanceCounter` data.
- Added lifecycle state guard properties, action confirmations, kill countdown confirmation, and stopped-only delete rejection.
- Added six-language i18n keys for start confirmation and unavailable lifecycle actions.
- Restored instance card resource rows to daemon-card-style `ui:ProgressBar` controls with fixed padding and column widths.
- Fixed instance card `MenuFlyout` command bindings by storing lifecycle commands on each card model.
- Changed event ruleset non-match logging from "evaluation failed" to a debug-level skip and reject unsupported rulesets explicitly.
- Added SLP source-generated STJ metadata and a reflection-disabled regression test for Minecraft server ping payload parsing.
- Re-enabled daemon STJ reflection fallback for runtime compatibility while keeping disabled-fallback protocol tests for trim/AOT-sensitive paths.
- Published daemon successfully for `win-x64` and temporarily removed instance card progress bars to isolate the WPF stack overflow trigger.
- Normalized instance CPU and memory counters at the shared contract boundary, including source-generated JSON deserialization, and clamped WPF performance displays.
- Hardened the WPF exception dialog against recursive stack overflow by removing modern window chrome/backdrop and adding unhandled-exception reentrancy guards.
- Temporarily disabled instance-manager CPU/memory data population to isolate the remaining stack overflow trigger.
- Removed instance-card more-menu `x:Reference` bindings to avoid WPF `MarkupExtension` circular dependency during XAML load.
- Restored instance-card CPU and memory text display after isolating the stack overflow to more-menu `x:Reference` bindings.
- Localized instance status badge text and restored daemon-card-style CPU/memory progress bars on instance cards.
- Fixed instance-card CPU/memory progress bar bindings by setting read-only computed value bindings to `Mode=OneWay`.
- Aligned WPF instance action guards with daemon active-state semantics for `Starting` and `Running`.
- Made daemon and instance manager cards stretch together to fill available rows, using minimum width as the only sizing threshold.
