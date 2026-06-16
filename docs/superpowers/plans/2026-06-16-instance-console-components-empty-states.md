# Instance Console Components And Empty States Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve instance console component safety and empty states while aligning the instance settings Java runtime card with create-instance behavior.

**Architecture:** Keep all changes inside the WPF client. Extract reusable component scanning from `ComponentManagerViewModel` so both the component manager page and instance console window can identify client-side mods without duplicating daemon file download logic. Use existing `StopTipLayer` for disabled and empty containers, and keep user-facing text resource-backed.

**Tech Stack:** C# 14, WPF, CommunityToolkit.Mvvm, iNKORE.UI.WPF.Modern, existing daemon client file APIs.

---

## Touched Areas

- `frontend`: WPF instance console, component manager, event trigger, and instance settings UI.
- `tests`: targeted build/i18n verification; protocol tests if committing later.
- `agent-docs`: implementation plan and changelog.

## Tasks

- [x] Create a reusable WPF component scanning service/helper for mods/plugins directories.
- [x] Add instance console startup warning for enabled client-side mods with disable/ignore choices.
- [x] Keep client-side mod badge visible in component manager rows.
- [x] Add component manager unavailable state when neither mods nor plugins folder is supported.
- [x] Add component manager empty states with `StopTipLayer` for empty mods/plugins tabs.
- [x] Change add button text between add mod/add plugin by selected tab.
- [x] Add event trigger empty state with `StopTipLayer` when no rules exist.
- [x] Fix instance settings Java runtime card: disable until scan completes, preserve current path display, and preselect matching scanned runtime using create-instance display/path logic.
- [x] Add/update six-language i18n keys.
- [x] Verify strict i18n, WPF build, and diff hygiene.

## Changelog

- Added a shared WPF `ComponentScanner` for mods/plugins directory detection, jar metadata parsing, and enable/disable renaming.
- Instance console startup now warns about enabled client-side-only mods and can disable them in one action.
- Component manager now shows client-side badges, unsupported-loader and empty-tab `StopTipLayer` states, and tab-specific add button text.
- Component manager hides the top add/refresh toolbar when component management is unsupported, leaving only the unsupported-state refresh action.
- Event trigger rules now show a `StopTipLayer` placeholder when no rules exist.
- Instance settings Java runtime selection now mirrors create-instance display behavior, keeps the saved value as the Java path, and disables the runtime card until daemon Java scanning finishes.
- Added six-language i18n keys for the new warnings, empty states, and add actions.
- Verified with WPF build, strict i18n scan, and `git diff --check`.
