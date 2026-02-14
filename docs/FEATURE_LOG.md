# Feature Log — Theme Engine & Theme Editor (2025-10-19)

This document records the design, contract, implementation notes, and files changed for the recent Theme Engine and Theme Editor work. Use this as a single-source quick reference for reviewers and future maintenance.

## Feature: Live Theme Preview (ApplyThemePreview)

- Overview: Allow users to preview color and gradient edits in the Theme Editor immediately without restarting the app.

- Contract:
  - Input: `ThemeDefinition` object containing ColorOverrides and Gradients.
  - Output: Application UI updates to reflect color and gradient changes in-place.
  - Error modes: invalid color/gradient definitions must be logged and ignored; UI must remain stable.
  - Success criteria: After applying a preview, `Application.Current.Resources` contains updated gradient and color keys (e.g., `App.TitleBarBackground`) and open windows reflect the changes immediately.

- Edge cases:
  - Missing or malformed gradient stops
  - Duplicate resource keys already present in resources
  - Interactions with legacy AXAML hardcoded brushes

- Files changed / created:
  - Services/ThemeEngine.cs — added `ApplyThemePreview`, improved `ApplyGradients`, added (DEBUG-gated) `LogResourceDiagnostics`, and enhanced window refresh behavior.
  - Services/ThemeService.cs — preserved `App.TitleBarBackground` and clarified comments on legacy behavior.
  - Resources/Themes/*.zttheme — legacy themes updated to include titlebar gradient definitions.
  - Views/ThemeEditorWindow.axaml & ViewModels/ThemeEditorViewModel.cs — UI/VM changes for live editing, Save/SaveAs fixes.
  - Styles/*.axaml — removed hardcoded `App.TitleBarBackground` brushes.

- Implementation notes:
  - To ensure runtime precedence, gradients are added to the active window's `app.Resources` and also to `Application.Current.Resources`.
  - Apply process removes existing resource keys before creating new brushes to avoid stale instances.
  - Log statements help trace resource presence through the application lifecycle; they are gated to DEBUG to reduce noise in release builds.

## Feature: Theme Editor UX Fixes

- Overview: Improve color picker behavior, hex input layout, Save/SaveAs, and duplicate GUID handling.

- Files changed:
  - Controls/ColorPicker/* — adjustments for sizing and hex input.
  - ViewModels/ThemeEditorViewModel.cs — Save/SaveAs behavior, GUID duplication checks.
  - Views/ThemeEditorWindow.axaml — UI layout changes.

- Acceptance criteria:
  - Hex input displays correctly and updates color fields.
  - Save/SaveAs writes correct GUIDs and prevents duplicates.
  - Gradient preview updates without requiring restart.

## Diagnostics & Observability

- `bin\Debug\net9.0\logs\theme_engine.log` contains diagnostic traces that were used during development and repro; statements include resource presence checks and gradient application traces.
- Diagnostics are useful for future regression investigations; keep them gated behind DEBUG.

## Follow-ups / Next steps

- Add a small integration test targeting `ApplyThemePreview` to assert that `Application.Current.Resources` receives `App.TitleBarBackground` after applying a sample theme.
- Optionally add a runtime toggle or logger-level control to enable diagnostics in non-debug builds.
- Backfill older changelog entries if needed.

