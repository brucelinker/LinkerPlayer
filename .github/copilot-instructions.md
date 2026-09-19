# LinkerPlayer — Copilot Instructions

WPF music player, **.NET 10 (net10.0-windows, x64)**. Three projects: `LinkerPlayer` (main app), `LinkerPlayer.BassLibs` (BASS audio SDK wrapper), `LinkerPlayer.Tests` (xUnit).

**Quick Section Map:**
- [Core Architecture](#architecture-constraints) — MVVM, DI, anti-patterns
- [The Library](#the-library-most-important-feature) — single source of truth for tracks
- [Playlists](#playlists-second-most-important-feature) — mutation entry point
- [Selection Persistence](#selection-persistence-across-tabs-critical-ux-requirement) — tab state survival
- [Playback](#playback) — play/pause/seek/skip
- [Coding & Testing](#coding-rules) — style, async rules, xUnit
- [Common Pitfalls](#common-pitfalls--quick-reference) — avoid these mistakes
- [Reference](#reference) — file locations, package pins

## General Guidelines
- Use a developer-technical tone in documentation rather than a marketing-style tone.

## Architecture constraints
- **MVVM** via `CommunityToolkit.Mvvm`. All ViewModels use `[ObservableProperty]` / `[RelayCommand]` source generators on `partial` classes. Prefer MVVM-first architecture with minimal code-behind and simpler binding-first solutions over adding more event-driven logic. **Let WPF be WPF**; avoid architecture-fighting, churn-inducing code paths.
- **DI** via `Microsoft.Extensions.Hosting` — register all services and ViewModels in `App.xaml.cs`; always inject via constructor against the interface, never the concrete type; no static mutable state.

## Known pain points & anti-patterns (do not reintroduce)
- Duplicate logic for track selection, tab state, or library queries across ViewModels — centralize in services (`IMusicLibrary`, `IPlaylistManagerService`, or a new `ISelectionStateService`).
- "Desperate" hacks (direct collection mutation, long-lived contexts, static state, `.Result`/`.Wait()`, heavy use of `!` null suppression).
- Inconsistent selection handling across Library tab vs. Playlist tabs — see dedicated section below.
- Code that bypasses `IMusicLibrary.AddTracksToLibraryBatchAsync` / `DatabaseSaveService` or `IPlaylistManagerService` for mutations.
- **Messaging** — use `WeakReferenceMessenger` with strongly-typed message records in `LinkerPlayer/Messages/`. Add new messages there.
- **EF Core 10 / SQLite** — always use `IDbContextFactory<MusicLibraryDbContext>` with short-lived `await using` contexts. Never hold a long-lived `DbContext`.
- **Audio** — `IAudioEngine` / `ISpectrumPlayer` abstractions; BASS implementation is in `LinkerPlayer.BassLibs`. Output modes: DirectSound and WASAPI.
- Flag churn candidates by identifying code smells like ad-hoc 'Fix/Force/Shimmy' style patch logic; prefer deleting such workaround code.

## The Library (most important feature)
`IMusicLibrary` / `MusicLibrary` (`Core/MusicLibrary.cs`) is the **single source of truth** for all tracks and playlists.
- **Never** mutate `MainLibrary` (`RangeObservableCollection<MediaFile>`) or the DB directly from ViewModels or Services — always go through `IMusicLibrary` async methods.
- Import pipeline: `FileImportService` → `AddTracksToLibraryBatchAsync` → `DatabaseSaveService` (debounced).
- `WatchedFolderService` feeds new files into the same pipeline. `BackgroundMetadataRefresher` handles stale metadata.
- Supported extensions: `MusicLibrary._supportedAudioExtensions`. Cover art: `CoverManager` / `CoverProber`.
- `TrackHealthStatus` enum: `Unknown / Ok / Missing / Changed`.
- **ReplayGain classification rule:** Set Track when only track ReplayGain values exist; set Album when both track and album values exist.

## Playlists (second most important feature)
`IPlaylistManagerService` / `PlaylistManagerService` (`Services/PlaylistManagerService.cs`) is the **single entry-point** for all playlist mutations (create, rename, delete, add/remove/reorder tracks).
- UI model: `PlaylistTab` wraps `Playlist` with a live `ObservableCollection<MediaFile>`.
- `PlaylistTabsViewModel` (+ `.DragDrop.cs`) drives the tab strip and drag-drop reordering of both tracks and tabs.
- `PlaylistsNET` handles M3U/PLS import/export.

## Selection persistence across tabs (critical UX requirement)
**Goal:** "It just works" like a proper music player — selection survives tab switches, restarts, and reordering without manual re-clicking or churny visible fixes.

- Each tab (Library + every `PlaylistTab`) independently remembers its selected `MediaFile`.
- **Never** store selection only in UI control (`SelectedItem`). Always mirror it in ViewModel (`PlaylistTab.SelectedMediaFile` or `PlaylistTabsViewModel.CurrentSelection`).
- Two-way bind `SelectedItem` in XAML; use `SelectionChanged` to persist/broadcast changes via `WeakReferenceMessenger` (messages in `LinkerPlayer/Messages/`).
- **Session persistence:** Store in `PlaylistTab.SelectedMediaFile` (ObservableProperty).
- **Restart persistence:** Store last-selected track ID in DB or user settings; restore on `PlaylistTabsViewModel` load.
`MediaTabViewModel.DragDrop.cs` must preserve selection during reorder.
- Keep `IsSynchronizedWithCurrentItem="False"` on track grids (prevents first-row jumps after refresh).
- **No imperative `SelectedItem` writes** in ViewModels during startup. Let binding + `SelectionChanged` do the work.
- When selection clears unexpectedly during startup, refresh, or tab switching, find the code path that is clearing it and remove or reorder that path. Do not add a later 'restore selection' workaround just to reapply state after the fact.
- User **dislikes** 'restore'/'fixup' patterns — prefer correct ordering so state is set once at the right time without deferred scrolling or post-actions. Fix for Library deselection involves removing redundant code paths (like double loading) and centralized restore via events.

## Playback
`IPlaybackCoordinator` / `PlaybackCoordinator` owns the play/pause/stop/seek/skip lifecycle. `TrackNavigationService` resolves next/previous (shuffle via `PlaybackCursor`). Crossfade and smooth-stop are in `AudioEngine.Crossfade.cs`.
- Validate playback fixes behaviorally; code changes that do not change runtime behavior are not acceptable.

## Coding rules
- Nullable reference types **enabled** — annotate correctly; avoid `!` suppressions.
- Implicit usings **enabled** — don't add `using System;` etc. unless a type genuinely requires it.
- **No** `.Result` or `.Wait()` — `async`/`await` throughout.
- Code style enforced by `.editorconfig` + `EnforceCodeStyleInBuild=true` — build to verify before finishing.
- Prefer aggressive simplification and deleting redundant code paths when refactoring, while preserving behavior. Use standard event/method names like `OnTabSelectionChanged`.
- Prioritize code removal/simplification; revert ineffective added code rather than keeping speculative logic. **User strongly prefers deleting unnecessary code paths and simplifying behavior rather than adding guards or workaround logic.** Revert regressions immediately when a deletion causes worse behavior (e.g., loss of Library selection entirely). Immediate reverts are required when proposed fixes do not work, especially for the Library deselection issue.

## Testing rules
- xUnit; fakes in `Fakes/`, test doubles in `Mocks/`. Name: `Method_Scenario_ExpectedBehavior`.
- No real filesystem or real DB — use in-memory EF or fakes.

## Simplification-first approach
- **Remove code, don't add it.** When something breaks (e.g., selection clears), find and DELETE the code that's causing it, not add guards/patches.
- Example: Track selection was deselecting at startup because `RestoreFilterSelections()` called `RefreshView()` which reset the CollectionView. **Fix: Remove the `RefreshView()` call**, don't add workaround code.
- Remove any new code that has no observable effect.
- Prefer minimal WPF list/selection logic over layered event handling.
- Simplify by removing unnecessary code paths/messages instead of adding new abstractions when existing `SelectionService`/`SharedDataModel` already cover the behavior.
- Aggressively remove thin wrapper methods and reduce indirection when it doesn't lose needed behavior.
- **Revert immediately** if a deletion causes regressions (e.g., loss of Library selection). Immediate reverts are required when proposed fixes do not work.

## Reference

### Key files & services
| File/Service | Purpose |
|---|---|
| `Core/MusicLibrary.cs` | Single source of truth for all tracks; all mutations via `IMusicLibrary` async methods |
| `Services/PlaylistManagerService.cs` | Single entry-point for all playlist mutations (create/rename/delete/reorder) |
| `Services/PlaybackCoordinator.cs` | Play/pause/stop/seek/skip lifecycle |
| `Services/TrackNavigationService.cs` | Next/previous track resolution with shuffle support |
| `Services/FileImportService.cs` | Track import pipeline → `AddTracksToLibraryBatchAsync` |
| `Services/WatchedFolderService.cs` | Auto-detect and import new files |
| `Services/BackgroundMetadataRefresher.cs` | Refresh stale track metadata |
| `ViewModels/PlaylistTabsViewModel.cs` (+ `.DragDrop.cs`) | Tab strip and reordering orchestration |
| `Messages/` | Strongly-typed `WeakReferenceMessenger` message records |
| `App.xaml.cs` | DI registration for all services and ViewModels |
| `.editorconfig` | Code style enforcement; check on build |

### Package versions (do not upgrade without consideration)
| Package | Version |
|---|---|
| CommunityToolkit.Mvvm | 8.4.2 |
| ManagedBass / .Fx / .Mix / .Wasapi | 4.0.2 |
| MaterialDesignThemes | 5.3.2 |
| EF Core + SQLite | 10.0.7 |
| z440.atl.core | 7.13.0 |
| PlaylistsNET | 1.4.1 |

## Common pitfalls & quick reference
- **Selection issues?** Suspect WPF lifecycle events (load/refresh/virtualization), not logic. Avoid imperative `SelectedItem` writes or delayed dispatcher choreography. Prefer deleting stale patches.
- **Tab switching breaks selection?** Check `PlaylistTab.SelectedMediaFile` binding and `SelectionChanged` persistence path — one path only.
- **First row selects after refresh?** Ensure `IsSynchronizedWithCurrentItem="False"`.
- **Mutations affecting Library?** Always go through `IMusicLibrary` methods or `IPlaylistManagerService`, never direct collection edits.
- **Async hangs?** No `.Result`, `.Wait()`, or blocking calls — use `async`/`await`.
- **Null warnings?** Annotate correctly (nullable ref types on); avoid `!` suppressions.

## User Preferences
- Minimize and simplify code; revert ineffective added code rather than keeping it. User prefers simplification-first changes that remove root causes instead of adding workaround logic; dislikes naming and patterns like 'fix'/'patch'/'shimmy' that imply ongoing corrective churn.
