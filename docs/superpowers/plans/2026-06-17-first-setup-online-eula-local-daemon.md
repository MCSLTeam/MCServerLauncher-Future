# First Setup Online EULA And Local Daemon Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the built-in WPF EULA text with a language-specific online EULA gate and add a first-setup daemon choice for remote hosts or local daemon download.

**Architecture:** Keep the flow inside `MCServerLauncher.WPF/View/FirstSetupHelper`. Extract small deterministic helpers for EULA URL selection and local daemon path/download validation so behavior is easy to verify without driving WPF dialogs. Use existing `Lang.Tr[...]`, `ContentDialog`, and iNKORE button styles.

**Tech Stack:** C# 14, WPF, iNKORE.UI.WPF.Modern, `System.Net.Http`, `System.Diagnostics.Process`, `.resx` localization.

---

## Touched Areas

- `frontend`: first setup pages and localized WPF strings.
- `docs`: this implementation plan and changelog.
- `storage`: local daemon executable is written beside the launcher executable when the URL is configured.
- `tests`: WPF build and protocol test command before commit.

## File Structure

- Modify `MCServerLauncher.WPF/View/FirstSetupHelper/EulaSetupPage.xaml`: remove embedded EULA text and show online EULA prompt.
- Modify `MCServerLauncher.WPF/View/FirstSetupHelper/EulaSetupPage.xaml.cs`: add 15-second countdown, language URL selection, browser opening, and accept flow.
- Modify `MCServerLauncher.WPF/View/FirstSetupHelper/DaemonSetupPage.xaml`: add remote/local mode buttons and contextual action area.
- Modify `MCServerLauncher.WPF/View/FirstSetupHelper/DaemonSetupPage.xaml.cs`: add mode switching, local daemon download placeholder, and “add another host” prompt.
- Modify `MCServerLauncher.WPF/View/FirstSetupHelper/FirstSetup.xaml.cs`: add a debug-only restart entry that returns the first setup UI to the language page.
- Modify `MCServerLauncher.WPF/View/FirstSetupHelper/WelcomeSetupPage.xaml.cs`: route setup completion through `FirstSetup` so debug preview does not rewrite persisted setup state.
- Modify `MCServerLauncher.WPF/MainWindow.xaml.cs`: expose a debug-only method that shows the first setup overlay without changing persisted setup flags.
- Modify `MCServerLauncher.WPF/View/Pages/DebugPage.xaml`: add a first setup test button in the existing debug window/notification test section.
- Modify `MCServerLauncher.WPF/View/Pages/DebugPage.xaml.cs`: route the debug button to the main window first setup overlay.
- Modify `MCServerLauncher.WPF/Translations/Lang.*.resx`: add first setup strings for online EULA and local daemon mode.
- Update this plan changelog before finishing.

## Tasks

### Task 1: Rewrite EULA Page Behavior

- [x] **Step 1: Replace embedded EULA content**

Remove `Eula1` through `Eula19` bindings from `EulaSetupPage.xaml`. Add a compact content area with explanatory localized text, current EULA URL, and an “open EULA” button.

- [x] **Step 2: Add URL selection and countdown**

In `EulaSetupPage.xaml.cs`, map languages to URLs:

```csharp
private static string GetEulaUrl(string? language)
{
    return language switch
    {
        "en-US" => "https://future.mcsl.com.cn/en/eula.html",
        "ja-JP" => "https://future.mcsl.com.cn/ja/eula.html",
        "ru-RU" => "https://future.mcsl.com.cn/ru/eula.html",
        "zh-HK" or "zh-TW" => "https://future.mcsl.com.cn/zh-hant/eula.html",
        _ => "https://future.mcsl.com.cn/eula.html"
    };
}
```

Use `DispatcherTimer` to enable the accept button after 15 seconds.

- [x] **Step 3: Open browser and accept**

Use `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })` for opening the EULA. Keep the existing refusal confirmation. On accept, show the existing final confirmation and call `parent?.GoDaemonSetup()`.

### Task 2: Add Daemon Setup Mode Selection

- [x] **Step 1: Add remote/local choice UI**

Add two side-by-side buttons below the daemon setup subtitle. The selected button uses `AccentButtonStyle`; the unselected button uses the default button style. Default selection is remote host.

- [x] **Step 2: Keep remote host path**

Remote mode keeps the existing `ConstructConnectDaemonDialog()` flow and `TryConnectDaemon(...)`.

- [x] **Step 3: Add local daemon download placeholder**

Add a blank URL constant:

```csharp
private const string LocalDaemonDownloadUrl = "";
```

If the URL is blank, show a localized warning and do not write files. If the URL is configured later, download to:

```csharp
Path.Combine(AppContext.BaseDirectory, "daemon.exe")
```

- [x] **Step 4: Ask whether to add another host**

After a successful remote add or local download, show a `ContentDialog` asking whether the user wants to add another host. If yes, stay on the daemon setup page. If no, enable continue and move to welcome.

### Task 3: Localize New UI Strings

- [x] **Step 1: Add resource keys**

Add these keys to all six language `.resx` files:

```text
FirstSetup_EulaOnlineTip
FirstSetup_EulaOpenInBrowser
FirstSetup_EulaContinueCountdown
FirstSetup_EulaUrlLabel
FirstSetup_DaemonRemoteHost
FirstSetup_DaemonLocalHost
FirstSetup_DaemonLocalDownload
FirstSetup_DaemonLocalDownloadUnavailable
FirstSetup_DaemonAddAnotherHostTitle
FirstSetup_DaemonAddAnotherHostTip
FirstSetup_DaemonAddAnotherHost
FirstSetup_DaemonFinishAdding
```

- [x] **Step 2: Keep legacy EULA keys untouched**

Do not remove `Eula1` through `Eula19` from resource files in this task. The UI no longer references them, and keeping them avoids translation churn.

### Task 4: Verification

- [x] **Step 1: Build WPF**

Run:

```powershell
dotnet build MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1
```

Expected: exit code 0.

- [x] **Step 2: Run required protocol tests**

Run:

```powershell
dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --no-build
```

Expected: exit code 0. If `--no-build` lacks Release artifacts, report that explicitly and run the smallest required build/test follow-up.

- [x] **Step 3: Final hygiene**

Run:

```powershell
git diff --check
git status --short --branch
```

Expected: no whitespace errors; changed files are scoped to this task.

### Task 5: Rework Daemon Setup Layout With iUWM Controls

- [x] **Step 1: Use an iUWM page root and two-column welcome layout**

Change `DaemonSetupPage.xaml` from a top-stacked WPF `Page` layout to an iNKORE `ui:Page` root with a left prompt column and a right setup column, matching the visual rhythm of the supplied welcome-page reference.

- [x] **Step 2: Replace mode buttons with `ui:RadioButtons`**

Use `ui:RadioButtons` for the remote/local host choice. Keep the selected visual inside the iNKORE control instead of manually toggling two separate button styles.

- [x] **Step 3: Keep existing daemon action behavior**

Keep `ConnectDaemon`, local daemon download placeholder, daemon list, skip, and continue behavior unchanged. Update code-behind to read `DaemonModeRadioButtons.SelectedIndex`.

- [x] **Step 4: Verify WPF build**

Run:

```powershell
dotnet build MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1
```

Expected: exit code 0.

### Task 6: Add Debug Entry For First Setup

- [x] **Step 1: Add a restart method to the first setup control**

Add `RestartForDebug()` to `FirstSetup.xaml.cs` so the test entry can return the embedded setup control to its first page without resetting settings:

```csharp
public void RestartForDebug()
{
    Opacity = 1;
    RefreshNavMenu(0);
}
```

- [x] **Step 2: Add a main window debug display method**

Add `ShowFirstSetupForDebug()` to `MainWindow.xaml.cs` so DebugPage does not reach into named controls directly:

```csharp
public void ShowFirstSetupForDebug()
{
    SetupView.RestartForDebug();
    SetupView.Visibility = Visibility.Visible;
    NavView.Visibility = Visibility.Hidden;
    TitleBarRootBorder.Visibility = Visibility.Hidden;
}
```

- [x] **Step 3: Add a DebugPage button**

Add a `Show First Setup` button to the existing debug test group and handle its click by calling `Application.Current.MainWindow` when it is a `MainWindow`.

### Task 7: Fix First Setup Debug Preview Side Effects

- [x] **Step 1: Track debug sessions inside `FirstSetup`**

Add an `IsDebugSession` property. Set it to `true` from `RestartForDebug()` and reset it after finishing the overlay.

- [x] **Step 2: Avoid persisting first setup flags during debug preview**

Move setup completion persistence behind `FirstSetup.CompleteSetup()`. Keep normal first setup behavior unchanged, but skip `App.IsFirstSetupFinished` and `App.IsAppEulaAccepted` writes while `IsDebugSession` is true.

- [x] **Step 3: Show existing daemon nodes in debug preview**

When `DaemonSetupPage` becomes visible, load cards from `DaemonsListManager.Get` even if first setup is already finished and the parent first setup control is in debug mode.

- [x] **Step 4: Avoid duplicate daemon add/reconnect side effects while rendering existing nodes**

Render existing daemon cards without calling `TryConnectDaemon()`, because that path adds configs and can disturb existing WebSocket connection state.

### Task 8: Reorder Daemon Setup Into Local-First Flow

- [x] **Step 1: Replace the remote/local selector with a local runtime question**

Change `DaemonSetupPage.xaml` so the right column first asks whether to run the daemon locally. Show two iUWM-styled action buttons: local yes and remote/no.

- [x] **Step 2: Route “no” directly to remote node setup**

When the user chooses not to run locally, hide the local question, show the existing remote node list, and use the main action button for adding a remote node.

- [x] **Step 3: Ask about remote nodes after local handling**

After local daemon download succeeds, ask whether to add a remote node. Primary action opens the remote setup state; secondary action continues to the welcome page.

- [x] **Step 4: Keep debug preview behavior scoped**

Debug preview should still show existing daemon nodes once the remote setup state is entered, without rewriting setup flags.

### Task 9: Remove Daemon Card Selection Behavior

- [x] **Step 1: Replace the daemon card selection host**

Change the daemon manager card host from `ui:GridView` to a plain `ItemsControl` with a horizontal `WrapPanel` items panel. Keep the existing card data template, menu commands, and responsive width converter.

- [x] **Step 2: Preserve card width calculation**

Bind card width to the new `ItemsControl` host width instead of the removed `ui:GridView` ancestor so daemon cards continue to fill rows based on the existing minimum width.

- [x] **Step 3: Verify WPF XAML compilation**

Run a WPF build with `/m:1`. If the normal output is locked by a running app, use a temporary `OutDir` and remove it after the check.

## Changelog

- Added online EULA first setup flow with language-specific URL mapping and a 15-second continue gate.
- Added remote/local daemon setup choice with local daemon download placeholder handling.
- Added localized first setup strings for all six WPF languages.
- Reworked daemon setup page toward a two-column iUWM layout with `ui:RadioButtons` for host mode selection.
- Added a Debug page entry to show the first setup overlay from the language page without resetting persisted setup state.
- Fixed first setup debug preview so existing daemon nodes are shown and preview exit does not rewrite setup flags or duplicate daemon connection entries.
- Reordered daemon setup into a local-first question, with remote node setup shown only after choosing remote mode or opting to add remote nodes after local setup.
- Removed daemon card selectable list behavior by rendering daemon cards through a plain `ItemsControl` instead of a selectable `GridView`.
