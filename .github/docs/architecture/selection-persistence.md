# Selection Persistence Architecture

## Overview

Selection persistence is a core UX requirement for LinkerPlayer. Users expect that switching between the Library tab and multiple Playlist tabs (and back) remembers the previously selected track in each tab independently. Selection should also survive app restarts where possible.

This document serves as the single source of truth for how selection state is managed, persisted, and restored. All ViewModels, Services, and Copilot interactions must follow these rules.

## Goals

- Independent per-tab selection (Library tab + each `PlaylistTab`).
- Selection survives:
  - Tab switching
  - Tab reordering (drag-drop) for playlist tabs only (Library tab is always index 0).
  - App minimize/restore.
  - App restart (best effort).
- No loss of selection during async operations (library refresh, metadata updates, import pipeline).
- Clean MVVM binding — no direct manipulation of UI controls for selection state.
- Minimal performance impact on large libraries (47k+ tracks).

## Current State / Known Issues

- Selection often resets on tab switch or after operations.
- Some code paths mutate collections directly or fail to re-apply selection after drag-drop or collection changes.
- Inconsistent handling between main Library tab and Playlist tabs.
- No reliable cross-restart persistence (or it's fragile).

## Core Components Involved

- `MediaTabViewModel`, `PlaylistTabViewModel`, `LibraryTabViewModel.cs`. `BaseTabViewModel.cs`
- `PlaylistTab` wrapper (contains live `ObservableCollection<MediaFile>`)
- `IMusicLibrary` / `MusicLibrary` (single source of truth for tracks)
- `IPlaylistManagerService` / `PlaylistManagerService`
- `WeakReferenceMessenger` for cross-component notifications
- DB via `IDbContextFactory<MusicLibraryDbContext>` (for persistence)

## Recommended Data Model

- Add to `PlaylistTab` (or a dedicated selection property on `PlaylistTabViewModel`):

  ```csharp
  [ObservableProperty]
  private MediaFile? selectedMediaFile;

  // Or for index-based (more robust with large collections)
  [ObservableProperty]
  private int selectedIndex = -1;
  ```

- For persistence across restarts:
  - Store `LastSelectedTrackId` (or similar) per Playlist (add column to Playlist entity) or a lightweight separate table `TabSelectionState` (PlaylistId or "Library", TrackId, Timestamp).
  - Library tab can use a global or user-settings fallback.

## Behavior Rules

1. **UI Binding**:
   - Two-way bind `SelectedItem` / `SelectedIndex` on the ListView/DataGrid to the VM property.
   - Handle `SelectionChanged` event to update the VM property (and publish message if needed).

2. **On Selection Change**:
   - Update the VM property.
   - Publish a strongly-typed message via `WeakReferenceMessenger` (e.g., `TrackSelectedMessage`) if other parts of the app (PlaybackCoordinator, etc.) need to react.
   - Persist asynchronously (debounced if necessary) to avoid blocking.

3. **On Tab Activation / Switch**:
   - Restore from stored state → set `SelectedMediaFile` / `SelectedIndex`.
   - Let binding update the UI.
   - If the track no longer exists (e.g., deleted or health status changed), gracefully fall back to first item or none.

4. **During Drag-Drop & Reordering** (in `MediaTabViewModel.DragDrop.cs`):
   - Preserve selection before operation.
   - Re-apply selection to the correct item after collection change.
   - Handle both track reordering within a tab and tab reordering.

5. **On App Startup / Library Load**:
   - After `IMusicLibrary` is populated and tabs are initialized, restore selections.
   - Use async patterns with `await using` for DB contexts.

6. **Edge Cases**:
   - Empty tabs/playlists.
   - During import/metadata refresh (use `TrackHealthStatus`).
   - Shuffle / crossfade / playback navigation.
   - Multiple windows/instances if supported.

## Implementation Guidance

- Prefer `SelectedIndex` over `SelectedMediaFile` for robustness with large dynamic collections.
- Centralize restore logic in `PlaylistTabViewModel` or a helper service.
- Add unit tests in `LinkerPlayer.Tests` using fakes (verify selection survives simulated tab switches and restarts).
- Follow all existing architecture rules: MVVM with CommunityToolkit, constructor DI, no statics, short-lived DbContexts, etc.

## Anti-Patterns (Avoid)

- Storing selection only in the UI element.
- Direct collection mutations bypassing services.
- Synchronous blocking calls.
- Forgetting to re-bind or re-apply after async operations.

## Related Files

- `copilot-instructions.md`
- `\.github\skills\persistent-tab-selection\SKILL.md` (actionable playbook)
- `PlaylistTabViewModel.cs`, `PlaylistTab.cs`, `PlaylistManagerService.cs`, `MusicLibrary.cs`

This document will evolve. Update it when the implementation changes.

Last updated: 2026-07-04
