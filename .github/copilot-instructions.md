# LinkerPlayer — Copilot Instructions

WPF music player, **.NET 10 (net10.0-windows, x64)**. Three projects: `LinkerPlayer` (main app), `LinkerPlayer.BassLibs` (BASS audio SDK wrapper), `LinkerPlayer.Tests` (xUnit).

## Architecture constraints
- **MVVM** via `CommunityToolkit.Mvvm`. All ViewModels use `[ObservableProperty]` / `[RelayCommand]` source generators on `partial` classes. Prefer MVVM-first architecture with minimal code-behind and simpler binding-first solutions over adding more event-driven logic.
- **DI** via `Microsoft.Extensions.Hosting` — register all services and ViewModels in `App.xaml.cs`; always inject via constructor against the interface, never the concrete type; no static mutable state.

## Known pain points & anti-patterns (do not reintroduce)
- Duplicate logic for track selection, tab state, or library queries across ViewModels — centralize in services (`IMusicLibrary`, `IPlaylistManagerService`, or a new `ISelectionStateService`).
- "Desperate" hacks (direct collection mutation, long-lived contexts, static state, `.Result`/`.Wait()`, heavy use of `!` null suppression).
- Inconsistent selection handling across Library tab vs. Playlist tabs — see dedicated section below.
- Code that bypasses `IMusicLibrary.AddTracksToLibraryBatchAsync` / `DatabaseSaveService` or `IPlaylistManagerService` for mutations.
- **Messaging** — use `WeakReferenceMessenger` with strongly-typed message records in `LinkerPlayer/Messages/`. Add new messages there.
- **EF Core 10 / SQLite** — always use `IDbContextFactory<MusicLibraryDbContext>` with short-lived `await using` contexts. Never hold a long-lived `DbContext`.
- **Audio** — `IAudioEngine` / `ISpectrumPlayer` abstractions; BASS implementation is in `LinkerPlayer.BassLibs`. Output modes: DirectSound and WASAPI.

## The Library (most important feature)
`IMusicLibrary` / `MusicLibrary` (`Core/MusicLibrary.cs`) is the **single source of truth** for all tracks and playlists.
- **Never** mutate `MainLibrary` (`RangeObservableCollection<MediaFile>`) or the DB directly from ViewModels or Services — always go through `IMusicLibrary` async methods.
- Import pipeline: `FileImportService` → `AddTracksToLibraryBatchAsync` → `DatabaseSaveService` (debounced).
- `WatchedFolderService` feeds new files into the same pipeline. `BackgroundMetadataRefresher` handles stale metadata.
- Supported extensions: `MusicLibrary._supportedAudioExtensions`. Cover art: `CoverManager` / `CoverProber`.
- `TrackHealthStatus` enum: `Unknown / Ok / Missing / Changed`.

## Playlists (second most important feature)
`IPlaylistManagerService` / `PlaylistManagerService` (`Services/PlaylistManagerService.cs`) is the **single entry-point** for all playlist mutations (create, rename, delete, add/remove/reorder tracks).
- UI model: `PlaylistTab` wraps `Playlist` with a live `ObservableCollection<MediaFile>`.
- `PlaylistTabsViewModel` (+ `.DragDrop.cs`) drives the tab strip and drag-drop reordering of both tracks and tabs.
- `PlaylistsNET` handles M3U/PLS import/export.

## Selection persistence across tabs (critical UX requirement)
- Each tab (Library + every `PlaylistTab`) must independently remember its selected `MediaFile` (or index) and restore it on tab switch, app restart, or after drag-drop/reordering.
- Never store selection only in the UI control (ListView/DataGrid `SelectedItem`). Always mirror it in the ViewModel (`PlaylistTab` or a dedicated selection property on `PlaylistTabsViewModel`).
- Use `WeakReferenceMessenger` to broadcast selection changes when needed (strongly-typed message records in `Messages/`).
- Persistence strategy:
  - For the current session: Keep in `PlaylistTab.SelectedMediaFile` (ObservableProperty) + `PlaylistTabsViewModel.CurrentSelection`.
  - Across restarts: Store last-selected track ID per playlist (or for Library) in the DB (new lightweight table or column on `Playlist`) or user settings. Restore on `PlaylistTabsViewModel` load / tab activation.
- `PlaylistTabsViewModel.DragDrop.cs` must preserve selection state during reorder operations.
- Binding: Two-way bind `SelectedItem` on the items control to the VM property; handle `SelectionChanged` to update `IMusicLibrary` current track if appropriate (but do **not** mutate library from here).
- Goal: "It just works" like a proper music player — selection survives tab switches and restarts without manual re-clicking.

## Playback
`IPlaybackCoordinator` / `PlaybackCoordinator` owns the play/pause/stop/seek/skip lifecycle. `TrackNavigationService` resolves next/previous (shuffle via `PlaybackCursor`). Crossfade and smooth-stop are in `AudioEngine.Crossfade.cs`.

## Coding rules
- Nullable reference types **enabled** — annotate correctly; avoid `!` suppressions.
- Implicit usings **enabled** — don't add `using System;` etc. unless a type genuinely requires it.
- **No** `.Result` or `.Wait()` — `async`/`await` throughout.
- Code style enforced by `.editorconfig` + `EnforceCodeStyleInBuild=true` — build to verify before finishing.
- Prefer aggressive simplification and deleting redundant code paths when refactoring, while preserving behavior. Use standard event/method names like `OnTabSelectionChanged`.

## Testing rules
- xUnit; fakes in `Fakes/`, test doubles in `Mocks/`. Name: `Method_Scenario_ExpectedBehavior`.
- No real filesystem or real DB — use in-memory EF or fakes.

## Package pins — do not upgrade without consideration
| Package | Version |
|---|---|
| CommunityToolkit.Mvvm | 8.4.2 |
| ManagedBass / .Fx / .Mix / .Wasapi | 4.0.2 |
| MaterialDesignThemes | 5.3.2 |
| EF Core + SQLite | 10.0.7 |
| z440.atl.core | 7.13.0 |
| PlaylistsNET | 1.4.1 |

## Simplification-first approach
- Remove any new code that has no observable effect.
- Prefer minimal WPF list/selection logic over layered event handling.

## Selection stabilization notes (carry forward to new sessions)
- Keep **one** tab-selection path; avoid duplicate "restore/reapply" chains across ViewModel + code-behind.
- Treat `TabControl_SelectionChanged` as minimal glue. Do not reintroduce heavy selection/scroll orchestration there.
- Avoid imperative `DataGrid.SelectedItem` writes in ViewModels during startup/load lifecycle.
- In this codebase, `SelectedItem` is intentionally bound in XAML and user selection is persisted via `SelectionChanged` handler; do not reintroduce extra backflow paths without evidence.
- Keep `IsSynchronizedWithCurrentItem="False"` on track grids to avoid first-row selection jumps after view refresh.
- If selection appears then clears, suspect passive WPF lifecycle events (load/refresh/virtualization) before adding new logic.
- Prefer deleting stale "restore" patches over adding new delayed dispatcher choreography.
