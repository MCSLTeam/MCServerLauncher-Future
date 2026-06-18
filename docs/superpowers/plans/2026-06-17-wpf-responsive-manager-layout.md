# WPF Responsive Manager Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the SettingsPage launcher setting bindings, make manager card rows fill their containers, align manager toolbars, add daemon search, and guard instance console lifecycle commands.

**Architecture:** Keep the existing WPF pages and controls. Adjust XAML layout primitives, reuse the existing `CardWidthConverter`, and avoid introducing new layout components unless the current controls cannot express the behavior.

**Tech Stack:** C# 14, WPF, iNKORE.UI.WPF.Modern, CommunityToolkit.Mvvm, project resource-backed i18n.

---

### Task 1: Fix Settings Bindings

**Files:**
- Modify: `MCServerLauncher.WPF/View/Pages/SettingsPage.xaml`

- [x] Change `More_FollowStartupForLauncher.Status` to bind to `FollowStartup`.
- [x] Change `More_AutoCheckUpdateForLauncher.Status` to bind to `AutoCheckUpdate`.
- [x] Verify the binding paths exist on `SettingsViewModel`.

### Task 2: Fill Manager Card Rows

**Files:**
- Modify: `MCServerLauncher.WPF/View/Converters/CardWidthConverter.cs`
- Modify: `MCServerLauncher.WPF/View/Pages/DaemonManagerPage.xaml`
- Modify: `MCServerLauncher.WPF/View/Pages/InstanceManagerPage.xaml`

- [x] Remove artificial reserved width from card width resources.
- [x] Make converter account for only the real inter-card gap and clamp finite invalid widths.
- [x] Keep only minimum card widths; rows should stretch to the available line width whenever there are enough cards.

### Task 3: Add Narrow Layout Adaptation

**Files:**
- Modify: `MCServerLauncher.WPF/View/Pages/DaemonManagerPage.xaml`
- Modify: `MCServerLauncher.WPF/View/Pages/InstanceManagerPage.xaml`
- Modify: `MCServerLauncher.WPF/View/Pages/ResDownloadPage.xaml`
- Modify: `MCServerLauncher.WPF/View/ResDownloadProvider/*.xaml`

- [x] Move manager page toolbars to wrapping panels so controls wrap instead of clipping.
- [x] Reduce page side margins on narrow screens.
- [x] Make resource download provider layouts stack vertically below a narrow width.

### Task 4: Right-Align Manager Toolbars And Add Daemon Search

**Files:**
- Modify: `MCServerLauncher.WPF/View/Pages/InstanceManagerPage.xaml`
- Modify: `MCServerLauncher.WPF/View/Pages/DaemonManagerPage.xaml`
- Modify: `MCServerLauncher.WPF/ViewModels/InstanceManagerViewModel.cs`
- Modify: `MCServerLauncher.WPF/ViewModels/DaemonManagerViewModel.cs`
- Modify: `MCServerLauncher.WPF/View/Pages/InstanceManagerPage.xaml.cs`

- [x] Align instance manager filters, search, auto-refresh, interval, and refresh button to the right side of the toolbar row.
- [x] Bind instance manager search text to the view model and include name, type, version, id, status, and daemon text in filtering.
- [x] Add daemon manager search box next to refresh controls.
- [x] Bind daemon manager card list to a filtered collection, searching friendly name, address, versions, system type, and connection status.
- [x] Preserve existing refresh and add-connection commands.

### Task 5: Guard Instance Console Terminal Commands

**Files:**
- Modify: `MCServerLauncher.WPF/InstanceConsole/View/Pages/CommandPage.xaml`
- Modify: `MCServerLauncher.WPF/ViewModels/CommandPageViewModel.cs`
- Modify: `MCServerLauncher.WPF/InstanceConsole/Modules/InstanceDataManager.cs`

- [x] Subscribe command page view model to `InstanceDataManager.ReportUpdated`.
- [x] Expose `CanStart`, `CanStop`, `CanRestart`, `CanKill`, and `CanSendCommand` based on the current `InstanceStatus`.
- [x] Add menu separators so lifecycle actions are grouped consistently with instance cards.
- [x] Add confirmation before start, stop, and restart.
- [x] Add countdown confirmation before kill.
- [x] Reject unavailable lifecycle actions with warning notifications instead of sending daemon actions.
- [x] Reject command send when the instance is not running or starting.

### Task 6: Verification

**Files:**
- Inspect all modified files.

- [ ] Run `dotnet build MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1`.
- [ ] Run `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --no-build`.
- [ ] Run `git diff --check`.

### Task 7: Remove Manager Card Multi-Select

**Files:**
- Modify: `MCServerLauncher.WPF/View/Pages/DaemonManagerPage.xaml`
- Modify: `MCServerLauncher.WPF/View/Pages/InstanceManagerPage.xaml`
- Modify: `MCServerLauncher.WPF/View/Pages/InstanceManagerPage.xaml.cs`
- Modify: `MCServerLauncher.WPF/ViewModels/InstanceManagerViewModel.cs`
- Modify: `MCServerLauncher.WPF/ViewModels/Models/InstanceCardModel.cs`

- [x] Render daemon cards with `ItemsControl` and a wrapping items panel instead of selectable `ui:GridView`.
- [x] Render instance cards with `ItemsControl` and a wrapping items panel instead of selectable `ui:GridView`.
- [x] Remove instance card checkboxes and the batch operation bar.
- [x] Remove instance selection state and batch command logic from the instance manager view model.
- [x] Verify WPF build and whitespace hygiene.

### Task 8: Reduce Instance Lifecycle Refresh Latency

**Files:**
- Modify: `MCServerLauncher.WPF/ViewModels/InstanceManagerViewModel.cs`

- [x] Optimistically mark the clicked instance card as running after daemon connectivity is confirmed.
- [x] Optimistically mark stop and force-close actions as stopped, keep restart displayed as running, and remove deleted cards from the visible list after delete succeeds.
- [x] Replace full post-command `RefreshAsync()` calls with lightweight in-place refresh so the card list is updated without clearing the page and showing the loading layer.
- [x] On lifecycle command failure, run the same lightweight refresh to restore the daemon-reported status.

### Task 9: Stabilize Manager Status And Menus

**Files:**
- Modify: `MCServerLauncher.Common/ProtoType/Instance/InstanceStatus.cs`
- Modify: `MCServerLauncher.Daemon/Management/Communicate/InstanceProcess.cs`
- Modify: `MCServerLauncher.Daemon/Console/Commands/InstanceCommand.cs`
- Modify: `MCServerLauncher.ProtocolTests/InstanceSettingsCoordinatorTests.cs`
- Modify: `MCServerLauncher.WPF/ViewModels/Models/InstanceCardModel.cs`
- Modify: `MCServerLauncher.WPF/ViewModels/InstanceManagerViewModel.cs`
- Modify: `MCServerLauncher.WPF/ViewModels/DaemonManagerViewModel.cs`
- Modify: `MCServerLauncher.WPF/View/Pages/InstanceManagerPage.xaml`
- Modify: `MCServerLauncher.WPF/View/Pages/DaemonManagerPage.xaml`
- Modify: `MCServerLauncher.WPF/InstanceConsole/Window.xaml.cs`
- Modify: `MCServerLauncher.WPF/ViewModels/CommandPageViewModel.cs`
- Modify: `MCServerLauncher.WPF/InstanceConsole/View/Pages/CommandPage.xaml`
- Modify: `MCServerLauncher.WPF/Translations/*.resx`

- [x] Collapse public daemon protocol status values to stable running/stopped/crashed states.
- [x] Stop daemon process monitoring from emitting startup/shutdown transitional statuses.
- [x] Add protocol coverage that rejects public `Starting` and `Stopping` status values.
- [x] Stop exposing start/stop transitional states as filter options.
- [x] Treat lifecycle commands optimistically in WPF so users see stable running/stopped states while daemon commands settle.
- [x] Preserve manager filtered collections in place so card refresh does not rebuild opened flyout anchors.
- [x] Make instance console navigation tolerate null content and non-page navigation items.
- [x] Rename destructive process kill UI text to close wording in all six languages.
- [x] Remove WPF optimistic instance status overrides after lifecycle commands.
- [x] Show daemon/system-info load failures explicitly instead of silently displaying zeroes or placeholder dashes.

## Changelog

- Fixed incorrect launcher setting binding paths in SettingsPage.
- Refined manager card width calculation so card rows fill the available width without artificial right-side gaps.
- Kept the instance manager toolbar wrapping, but reverted the daemon manager and resource download vertical-layout changes per user request.
- Fixed daemon-side startup registration so instances are treated as active while `StartAsync` is still completing.
- Moved the daemon manager toolbar below the title/subtitle to match the instance manager page layout.
- Added follow-up tasks for right-aligned manager toolbars, daemon search, and instance console lifecycle command guards.
- Right-aligned instance and daemon manager toolbars, added daemon search, and wired instance manager search filtering.
- Added instance console terminal lifecycle state guards, grouped command menu entries, confirmation prompts, and kill countdown confirmation.
- Removed daemon manager card selection by replacing the selectable card host with a plain wrapping `ItemsControl`.
- Removed instance manager card multi-select by deleting card checkboxes, the batch operation bar, selection state, batch commands, and the selectable card host.
- Reduced instance lifecycle command refresh latency by updating clicked cards immediately and using lightweight report refresh after start, stop, restart, force-close, and delete requests.
- Stabilized manager card menus by preserving filtered collections during refresh, collapsed protocol and daemon instance status output to stable states, fixed instance console navigation null handling, and renamed kill-facing UI copy to close wording.
- Removed WPF-side instance status optimistic overrides and replaced daemon/system-info `0` or `--` fallbacks with explicit not-loaded/load-failed UI text.
