# Window Scoped Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure notifications raised from secondary windows, especially InstanceConsole, render in that window instead of always appearing in MainWindow.

**Architecture:** Keep the existing `Notification.Push(...)` call surface so existing pages and view models do not need a broad refactor. Make `NotificationContainer` register one container per owning WPF window, resolve the active window at push time, and fall back to the main/default container when no window-specific container exists.

**Tech Stack:** C# 14, WPF, iNKORE.UI.WPF.Modern InfoBar controls.

---

## Touched Areas

- `frontend`: WPF notification container ownership and InstanceConsole host layout.
- `agent-docs`: implementation plan and changelog.
- `tests`: WPF build, diff hygiene, and protocol pre-commit test if committing.

## Diagnosis

- Current implementation creates a static `NotificationContainer.Instance`.
- `MainWindow.InitializeView` adds that singleton to `GlobalGrid`.
- `Notification.Push(...)` always calls `NotificationContainer.Instance.AddNotification(...)`.
- InstanceConsole and its view models use the same notification API, so their notifications are still inserted into the container parented by MainWindow.

## Tasks

- [x] Add per-window registration and active-window resolution to `NotificationContainer`.
- [x] Update `Notification.Push(...)` close-button and add-notification paths to use the resolved target container.
- [x] Register the main window with the default notification container.
- [x] Add an InstanceConsole-local notification container to the console window root grid.
- [x] Verify with WPF build and `git diff --check`.

## Changelog

- `NotificationContainer` now supports registering containers per owning WPF window.
- `Notification.Push(...)` resolves the active window's notification container before adding or removing an `InfoBar`.
- `MainWindow` registers the existing default notification container.
- `InstanceConsole.Window` creates and registers its own notification container so console notifications render in the console window.
- Verified `dotnet build MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1` with 0 warnings and 0 errors.
- Verified `git diff --check`; no whitespace errors were reported.
