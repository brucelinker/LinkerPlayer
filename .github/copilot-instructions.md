# LinkerPlayer — Copilot Instructions

WPF music player, **.NET 10 (net10.0-windows, x64)**. Three projects: `LinkerPlayer` (main app), `LinkerPlayer.BassLibs` (BASS audio SDK wrapper), `LinkerPlayer.Tests` (xUnit).

## Architecture constraints
- **MVVM** via `CommunityToolkit.Mvvm`. All ViewModels use `[ObservableProperty]` / `[RelayCommand]` source generators on `partial` classes.
- **DI** via `Microsoft.Extensions.Hosting` — register all services and ViewModels in `App.xaml.cs`; always inject via constructor against the interface, never the concrete type; no static mutable state.
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

## Playback
`IPlaybackCoordinator` / `PlaybackCoordinator` owns the play/pause/stop/seek/skip lifecycle. `TrackNavigationService` resolves next/previous (shuffle via `PlaybackCursor`). Crossfade and smooth-stop are in `AudioEngine.Crossfade.cs`.

## Coding rules
- Nullable reference types **enabled** — annotate correctly; avoid `!` suppressions.
- Implicit usings **enabled** — don't add `using System;` etc. unless a type genuinely requires it.
- **No** `.Result` or `.Wait()` — `async`/`await` throughout.
- Code style enforced by `.editorconfig` + `EnforceCodeStyleInBuild=true` — build to verify before finishing.

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
