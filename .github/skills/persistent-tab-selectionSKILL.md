---
name: persistent-tab-selection
description: Use this skill for any task involving track selection in Library or Playlist tabs, tab switching, drag-drop reordering, or restoring selection on startup/restart. Enforce independent per-tab selection state using PlaylistTab and WeakReferenceMessenger. Follow the selection persistence rules in copilot-instructions.md exactly.
---

# Persistent Tab Selection Skill

## Core Rules (from copilot-instructions.md)

- `PlaylistTab` (or `PlaylistTabViewModel`) owns `SelectedMediaFile` as `[ObservableProperty]`.
- Use `WeakReferenceMessenger` for cross-VM notification of selection changes.
- Persist last-selected track ID (per playlist ID or Library) so it survives app restarts.
- Never mutate `MainLibrary` or playlist collections directly — go through the proper services.
- Drag-drop in `MediaTabViewModel.DragDrop.cs` must preserve the selected item.

## Implementation Steps (follow in order)

1. Identify or introduce `SelectedMediaFile` / `SelectedIndex` on `PlaylistTab` and the main tabs ViewModel.
2. Bind the ListView/DataGrid `SelectedItem` to tab-owned selection and avoid duplicate imperative selection paths.
3. On selection change, persist from the active tab context (Library vs Playlist) and publish a strongly-typed message only when required.
4. On tab activation / app startup: Restore selection from persisted state (DB or settings) → set tab-owned property → let binding update the UI.
5. On drag-drop or reorder: preserve tab-owned selection and avoid adding delayed multi-pass reselect logic unless proven necessary.
6. Add or update unit tests using fakes (no real DB/filesystem).

## Anti-Patterns to Avoid

- Storing selection only in the visual control.
- One global "current track" that overwrites per-tab state.
- Forgetting to restore after async library refresh or metadata updates.

Reference: copilot-instructions.md → "Selection persistence across tabs" section.
Also see PlaylistTabsViewModel, PlaylistTab, PlaylistManagerService, and any existing selection-related code.
