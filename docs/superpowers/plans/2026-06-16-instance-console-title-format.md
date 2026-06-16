# Instance Console Title Format Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the InstanceConsole system window title and in-window title use `Console - instance name - daemon host [address]`.

**Architecture:** Keep the title formatting in `MCServerLauncher.WPF/InstanceConsole/Window.xaml.cs`, because the window already owns daemon config and instance identity. Read the current instance name from `InstanceDataManager.CurrentReport`, read the node name from `Constants.DaemonConfigModel`, update the WPF `Title` property with a plain-text equivalent, and render the in-window title as segmented text plus the same icons used by the main navigation.

**Tech Stack:** C# 14, WPF, iNKORE.UI.WPF.Modern, existing WPF i18n resources.

---

## Touched Areas

- `frontend`: InstanceConsole title text.
- `agent-docs`: implementation plan and changelog.
- `tests`: WPF build and diff hygiene.

## Tasks

- [x] Name the InstanceConsole header `TextBlock` so code-behind can update it.
- [x] Add a formatter for `ConsoleTitle - instance name - daemon friendly name [ws(s)://host:port]`.
- [x] Set the formatted title after InstanceDataManager finishes initial data loading.
- [x] Subscribe to report updates so the title follows later instance name changes.
- [x] Verify with WPF build and `git diff --check`.
- [x] Render the in-window title as `ConsoleTitle  [instance icon] instance name  [node icon] node name`.
- [x] Set the in-window title icons and dynamic instance/node names to `Opacity="0.615"`.

## Changelog

- InstanceConsole now writes the same formatted title to the system window title and the in-window title text.
- The title format is `ConsoleTitle - instance name - daemon friendly name [ws(s)://host:port]`; in Chinese this displays as `控制台 - 实例名称 - 宿主机 [地址]`.
- The title format was revised to `ConsoleTitle - Instance [instance name] - Node [node name]`; in Chinese this displays as `控制台 - 实例 [实例名称] - 节点 [节点名称]`.
- The in-window title was revised to `ConsoleTitle  [Package icon] instance name  [ThisPC icon] node name`, matching the main navigation icons for instance management and remote node management.
- The in-window title icons and dynamic instance/node names now use `Opacity="0.615"` so the console label keeps the primary visual weight.
- The title is refreshed after initial instance data loading and whenever the instance report updates, so later instance name changes can be reflected.
- Verified with WPF build using an isolated output directory and `git diff --check`.
