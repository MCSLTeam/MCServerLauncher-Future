# Instance Settings Page Redesign Plan

Date: 2026-06-16

## Touched Areas

- `frontend`: WPF instance settings page layout and styling.
- `workflow`: plan, WPF build, i18n check, and git hygiene.

## Goal

Rewrite the instance settings page so its card layout, spacing, and page rhythm match the create-instance page: a clear page title, scrollable card content, compact grouped controls, and action buttons that remain easy to scan.

## Tasks

- [x] Inspect the current instance settings page, view model bindings, and create-instance page layout.
- [x] Rework `InstanceSettingsPage.xaml` into a create-instance-style title/content layout.
- [x] Keep existing commands and bindings intact.
- [x] Verify XAML builds and i18n keys remain complete.
- [x] Add changelog notes before finishing.
- [x] Replace the header path display with the instance GUID only.
- [x] Build instance type candidates from `InstanceType` metadata so the current type remains selectable and Java runtime candidates are complete.
- [x] Port the create-instance Java runtime selection behavior into the settings view model: editable Java path, daemon Java scan, candidate display, and scan dialog.
- [x] Port the create-instance JVM argument behavior into the settings view model: add/remove argument rows and the JVM argument helper dialog.
- [x] Show the save button only when editable settings have unsaved changes.
- [x] Verify strict i18n lookup, WPF build, and diff hygiene after the second-round refinements.

## Changelog

- Rebuilt the instance settings page with a create-instance-style title/header area and scrollable card stack.
- Grouped basic identity, version, Java runtime, core replacement, and installer controls into compact cards with consistent padding.
- Replaced fragile boolean converter parameter usage with explicit data-trigger visibility styles.
- Kept existing view-model commands and settings bindings unchanged.
- Verified the WPF project builds with 0 warnings and 0 errors.
- Replaced the header full working-directory subtitle with the instance GUID.
- Expanded instance type choices from `InstanceType` metadata so all Minecraft Java runtime types are candidates and the current type remains selected.
- Added settings-page Java runtime selection behavior matching create-instance: editable path, cached candidate display, daemon scan, and select-JVM dialog.
- Added settings-page JVM argument behavior matching create-instance: add/remove rows and the JVM argument helper dialog.
- Changed the save button to appear only when the current form snapshot differs from the last refreshed/saved snapshot.
- Verified strict i18n lookup, WPF `/m:1` build, and `git diff --check` after the second-round refinements.
