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
3. On selection change, persist from the active tab context (Library vs Playlist) and avoid introducing extra notification paths unless there is a real consumer.
4. On tab activation / app startup: Restore selection from persisted state (DB or settings) → set tab-owned property → let binding update the UI.
5. On drag-drop or reorder: preserve tab-owned selection and avoid adding delayed multi-pass reselect logic unless proven necessary.
6. Add or update unit tests using fakes (no real DB/filesystem).

## Anti-Patterns to Avoid

- Storing selection only in the visual control.
- One global "current track" that overwrites per-tab state.
- Forgetting to restore after async library refresh or metadata updates.

Reference: copilot-instructions.md → "Selection persistence across tabs" section.
Also see PlaylistTabsViewModel, PlaylistTab, PlaylistManagerService, and any existing selection-related code.

## High-Risk Hotspots (documented: do not touch casually)

### 6) OnGoToActiveTrack and OnActiveTrackChanged
- File: `LinkerPlayer/UserControls/MediaTabPanel.xaml.cs` (`OnGoToActiveTrack` around line ~1233, `OnActiveTrackChanged` around line ~1330)
- Why high churn risk:
  - Heavy imperative coordination of tab switch + selection + scroll/focus.
  - Temporarily unsubscribes/re-subscribes `TracksTable_OnSelectionChanged`.
  - Directly sets ViewModel selection state after DataGrid manipulation.
- Playback UX risk: high (go-to-playing-track and auto-follow behavior can regress quickly).
- Recommendation:
  - Leave as-is unless doing a dedicated refactor.
  - Any change must be covered by targeted tests for tab switch, selection sync, and scroll/focus behavior.

### 7) RegenerateColumns mega-method
- File: `LinkerPlayer/UserControls/MediaTabPanel.xaml.cs` (`RegenerateColumns`, near the top of the file)
- Why high churn risk:
  - Full DataGrid column rebuild can reset DataGrid selection, sort, and layout state.
  - Can trigger downstream selection restore chains and re-entrancy paths.
- Behavior risk: high (stateful UI and restore logic are tightly coupled here).
- Recommendation:
  - Do not casually modify; defer to dedicated refactor.
  - Future refactor should extract responsibilities and reduce full rebuild frequency.
  - Guard with focused tests around selection persistence and column/sort state retention.
