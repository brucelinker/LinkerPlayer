Scanned. Below is a per-file method map with a short purpose and an importance score (1–10) based on runtime impact.

ViewModels/BaseTabViewModel.cs
•	RegisterBaseMessages() — Registers playback/active-track message handlers; base wiring for tab VMs. 9/10
•	OnActiveTrackChangedInternal(MediaFile? track) — Extension point for derived classes on active-track updates. 5/10
•	Cleanup() — Unregisters message handlers to prevent leaks/duplicate handling. 8/10

ViewModels/ColumnSelectorViewModel.cs
•	ColumnSelectorItem(...) — Creates one selectable column model (name/property/visible). 6/10
•	ColumnSelectorViewModel() — Seeds default column list and defaults. 7/10
•	ConfirmSelection() — Publishes selected columns to the app via message. 8/10

ViewModels/EqualizerViewModel.cs
•	EqualizerViewModel(...) — Initializes EQ state, JSON preset path, and loads presets. 8/10
•	OnBand0Changed(...) ... OnBand9Changed(...) — Pushes each slider’s gain to engine frequency bands. 9/10
•	SaveEqPresets() — Serializes presets to app data JSON. 7/10
•	LoadFromJson() — Loads presets from JSON or creates preset file scaffold. 7/10

ViewModels/LibraryTabViewModel.cs
•	InitializeLibraryTab(...) — Builds library tab, restores columns/filter state, handles migration. 9/10
•	WireUpLibraryEvents(...) — Hooks refresh events needed by library UI flow. 7/10

ViewModels/MainViewModel.cs
•	MainViewModel(...) — Composes top-level VMs and applies saved theme. 8/10
•	OnWindowLoaded() — Currently no-op hook. 2/10
•	OnWindowClosing() — Forces final DB/settings save before exit. 10/10

ViewModels/PlayerControlsViewModel.cs
•	PlayerControlsViewModel(...) — Initializes control state, settings sync, playback message handling. 9/10
•	PlayPause() — Command wrapper for play/pause behavior. 7/10
•	Stop() — Command wrapper for stop. 7/10
•	Next() — Command wrapper for next track. 7/10
•	Prev() — Command wrapper for previous track. 7/10
•	OnShuffleModeChanged(bool) — Persists shuffle setting and broadcasts shuffle message. 8/10
•	OnIsMutedChanged(bool) — Updates mute tracking and broadcasts mute message. 7/10
•	OnVolumeSliderValueChanged(double) — Updates engine volume + persistence + mute state coupling. 9/10
•	OnSettingsChanged(string) — Reacts to external settings updates. 6/10
•	OnPlaybackStateChanged(PlaybackState) — Mirrors playback state into VM. 7/10
•	CanPlayPause() — Enables play/pause command (currently always true). 4/10
•	GetVolumeBeforeMute() — Returns remembered pre-mute volume fallback. 5/10
•	UpdateVolumeAfterAnimation(...) — Applies volume post-UI animation transitions. 6/10
•	SaveSettingsOnShutdown(...) — Saves final volume setting. 6/10
•	PlayPauseTrack() — Core play/pause/resume/start logic with selection context. 10/10
•	PlayTrack() — Forces playback of selected track with coordinator + state updates. 10/10
•	StopTrack() — Stops playback and clears active state. 9/10
•	PreviousTrack() — Navigates backward with re-entrancy guard. 9/10
•	NextTrack() — Navigates forward with re-entrancy guard. 9/10
•	CurrentSeekbarPosition() — Computes normalized seek progress safely. 7/10

ViewModels/PlaylistTabsViewModel.cs
•	PlaylistTabsViewModel(...) — Main tab/playlist orchestration setup and registrations. 10/10
•	RegisterMessages() — Subscribes to playback/shuffle messages. 8/10
•	ApplySelectedColumns(...) — Updates/persists active column set if changed. 7/10
•	UpdateSelectedColumnNames(...) — Forces/persists selected columns list. 6/10
•	OnDataGridLoaded(...) — Captures grid reference and restores selection. 8/10
•	ResetSelectionState() — Clears selected/multi-selected state consistently. 8/10
•	OnLibraryLoaded(...) — Restores saved library selection after library load. 8/10
•	OnLibraryViewRefreshed(...) — Reapplies selection after library view refreshes. 7/10
•	OnSelectedTabIndexChanged(int) — Syncs tab switch state and persists selected tab index. 9/10
•	OnTabSelectionChanged(...) — Handles tab switch flow incl. lazy loading and restore logic. 10/10
•	RestoreLibrarySelection() — Restores library track selection by saved ID. 8/10
•	RestorePlaylistSelection(PlaylistTab) — Restores playlist selection by saved track ID. 8/10
•	LoadPlaylistTracksAndRestoreSelectionAsync(...) — Lazy-loads playlist tracks and reapplies selection. 8/10
•	OnTrackSelectionChanged(...) — Central selection sync/persistence/health-trigger path. 10/10
•	CheckHealthForTracks(...) — Background disk-health check for selected tracks. 7/10
•	SyncGridSelection(...) — Keeps DataGrid selection aligned with model selection. 7/10
•	OnDoubleClickDataGrid() — Commits current track selection for playback context. 7/10
•	UpdateColumns(...) — Rebuilds visible DataGrid columns. 6/10
•	LoadPlaylistTabs() — Builds library tab + playlist tabs + startup restore state. 10/10
•	ForceLibrarySelection(...) — Hard-selects library item in grid. 6/10
•	LoadSelectedPlaylistTracksAsync() — Loads selected playlist tracks lazily. 8/10
•	LoadOtherPlaylistTracksAsync() — Background loads non-selected playlist tracks. 7/10
•	NewPlaylist() — Creates and selects a new playlist tab. 8/10
•	LoadPlaylist() — Prompts for playlist file and delegates import/load. 7/10
•	AddFolder() — Imports folder tracks and optionally silence analysis + playlist add. 9/10
•	AddFiles() — Imports selected files and attaches to selected playlist/library. 9/10
•	NewPlaylistFromFolder() — Creates playlist directly from folder content. 8/10
•	RemovePlaylist(...) — Deletes playlist and normalizes selected-tab state. 8/10
•	RemoveTrack() — Removes selected track from playlist or library (with safety prompts). 9/10
•	RescanSelectedTracks() — Revalidates selected files, removes missing, refreshes metadata. 8/10
•	AnalyzeSilence() — Batch silence analysis with cancellation/progress persistence. 8/10
•	ClearSilenceAnalysis() — Clears stored silence analysis fields for selection. 6/10
•	PlayTrack() — Sends play message from tab/grid context. 8/10
•	SelectFirstTrackCommand() — Command wrapper for first-track select. 5/10
•	GetRatings() — Fetches MusicBrainz ratings for selected tracks. 7/10
•	ShowProperties() — Opens/activates singleton Properties window. 8/10
•	OnPlaybackStateChanged(...) — Updates local playback/active state bindings. 7/10
•	GetSelectedPlaylist() — Resolves selected tab to backing playlist entity. 9/10
•	LoadPlaylistFileAsync(string) — Full playlist-file load + unresolved-path recovery flow. 10/10
•	ExtractPathsFromPlaylistFile(string) — Parses playlist paths by format. 8/10
•	ExtractPathsFromM3u(string) — M3U parser with encoding fallback strategy. 8/10
•	NormalizeForCompare(string) — Normalizes strings for fuzzy comparisons. 6/10
•	LevenshteinDistance(string,string) — Edit-distance helper for fuzzy match. 6/10
•	TryFindClosestFileInDirectory(...) — Fuzzy file recovery in immediate directory. 7/10
•	TryFindClosestFileRecursively(...) — Recursive fuzzy file recovery helper. 7/10
•	TryFindClosestDirectoryInParent(...) — Fuzzy directory-name recovery helper. 6/10
•	EnsureSelectedTabExistsAsync() — Guarantees a target playlist tab exists before import. 7/10
•	OnShuffleChanged(bool) — Initializes/clears shuffle sequence from current list. 8/10
•	SelectFirstTrack() — Selects first track in current tab/grid safely. 7/10
•	CreatePlaylistFromFolderAsync(...) — End-to-end folder import into a newly created playlist. 9/10
•	RightMouseDownTabSelect(string) — Selects tab on context-click. 5/10
•	RenamePlaylistAsync(PlaylistTab) — Renames playlist and reverts on failure. 7/10
•	BeginRenameTab(object?) — Triggers rename UI mode via message. 6/10
•	ReorderTabs((int,int)) — Reorders tabs and persists/reverts based on DB success. 8/10
•	CancelAnalyzeSilence() — Cancels in-progress silence analysis. 6/10
•	NotifyDirtyStateChanged() — Debounced notification for dirty-track save state. 7/10
•	SaveDirtyTracksAsync() — Saves edited tags/art to files + persists DB, with progress. 10/10

ViewModels/PlaylistTabsViewModel.DragDrop.cs
•	DragOver(...) — Sets drag-drop visual effect availability. 5/10
•	Drop(...) — Entry point for dropped files/folders + import lifecycle control. 8/10
•	HandleDropAsync(...) — Classifies dropped items and dispatches batch handlers. 8/10
•	HandleSingleFileDropAsync(string) — Imports one file and appends to selected playlist. 7/10
•	HandleFolderDropAsync(...) — Routes folder drop to new-playlist or current-playlist path. 6/10
•	AddFilesToCurrentPlaylistAsync(...) — Batch-import dropped files into current playlist. 8/10
•	AddFolderToCurrentPlaylistAsync(...) — Imports dropped folder into current playlist. 8/10

ViewModels/PropertiesViewModel.cs
•	PropertiesViewModel(...) — Initializes metadata loaders, events, and debounce behavior. 9/10
•	SelectedTracks_CollectionChanged(...) — Debounces rapid selection changes. 7/10
•	SelectionDebounceTimer_Tick(...) — Executes deferred single/multi metadata load. 8/10
•	Ok() — Applies changes, updates model, requests close(success). 8/10
•	Apply() — Applies changes without closing window. 8/10
•	Cancel() — Handles unsaved-changes prompt then close(cancel). 7/10
•	SharedDataModel_PropertyChanged(...) — Handles selected-track switch + unsaved decision flow. 9/10
•	LoadTrackData(string) — Loads one file’s ATL metadata/properties into sections. 9/10
•	LoadMultipleTracksData(IEnumerable<MediaFile>) — Loads aggregate metadata for multi-selection. 9/10
•	LoadAllSectionsAtl(Track) — Populates all single-track sections and hooks edit handlers. 9/10
•	LoadAllSectionsMultipleAtl(...) — Populates all multi-track sections and aggregate values. 9/10
•	LoadReelPlaceholder() — Loads fallback reel image resource. 4/10
•	LoadAlbumCoverFromPictureInfo(...) — Builds BitmapImage from embedded art bytes. 6/10
•	SortMetadataItems() — Orders core tags first and custom tags after. 6/10
•	TagItem_PropertyChanged(...) — Marks VM as having unsaved changes. 8/10
•	ApplyChanges() — Commits edited tag data back to ATL tracks and saves files. 10/10
•	UpdateTrackMetadata() — Refreshes in-memory tracks and persists metadata updates to DB. 9/10
•	Dispose() — Unhooks timers/events and cancels running work. 8/10

ViewModels/PropertiesViewModel.Commands.cs
•	DetectBpmAsync() — Runs BPM analysis, updates metadata item + progress/status. 8/10
•	CancelBpmDetection() — Cancels BPM detection operation. 5/10
•	CanDetectBpm() — Availability rule for BPM command. 4/10
•	CanCancelBpmDetection() — Availability rule for BPM cancel command. 4/10
•	CalculateReplayGainAsync() — Computes ReplayGain/Peak and writes to ReplayGain fields. 8/10
•	CancelReplayGainCalculation() — Cancels ReplayGain calculation. 5/10
•	CanCalculateReplayGain() — Availability rule for ReplayGain command. 4/10
•	CanCancelReplayGainCalculation() — Availability rule for ReplayGain cancel command. 4/10

ViewModels/SharedDataModel.cs
•	SharedDataModel() — Initializes read-only selected-tracks view and change forwarding. 8/10
•	SafeUiInvoke(Action) — Ensures updates execute on UI dispatcher thread. 9/10
•	UpdateSelectedTrackIndex(int) — Updates selected index safely on UI thread. 7/10
•	UpdateSelectedTrack(MediaFile) — Updates selected track safely on UI thread. 8/10
•	UpdateActiveTrack(MediaFile?) — Updates active track safely on UI thread. 8/10
•	UpdateSelectedTracks(IEnumerable<MediaFile>) — Replaces multi-selection collection safely. 9/10

ViewModels/Properties/IAtlMetadataLoader.cs
•	Load(...) — Contract for single-track section loading. 7/10
•	LoadMultiple(...) — Contract for multi-track aggregate loading. 7/10

ViewModels/Properties/Loaders/CoreMetadataLoader.cs
•	Load(...) — Loads/edit-binds core tags for one track. 9/10
•	LoadMultiple(...) — Aggregates core tags across tracks with <various> semantics. 9/10
•	AddMetadataItem(...) — Internal helper to add one editable metadata item. 5/10
•	AddMetadataItemMultiple(...) — Internal helper to add aggregate multi-track metadata item. 7/10
•	UpdateAllFiles(...) — Applies one edited field across all selected tracks. 8/10

ViewModels/Properties/Loaders/CustomMetadataLoaderAtl.cs
•	Load(...) — Loads non-standard custom tags for one track. 8/10
•	LoadMultiple(...) — Aggregates custom tags across tracks and editability. 8/10
•	IsFieldEditable(...) — Determines custom-tag write support by format. 6/10
•	UpdateField(...) — Writes/removes a custom field in ATL additional fields. 7/10

ViewModels/Properties/Loaders/FilePropertiesLoader.cs
•	Load(...) — Loads technical read-only properties (duration/bitrate/etc.) for one track. 7/10
•	LoadMultiple(...) — Aggregates read-only technical properties across tracks. 7/10
•	AddPropertyItem(...) — Adds one read-only property if present. 4/10
•	AddPropertyItemMultiple(...) — Adds aggregated read-only property (<various> support). 5/10

ViewModels/Properties/Loaders/LyricsCommentLoaderAtl.cs
•	LoadComment(...) — Loads editable comment TagItem for one track. 7/10
•	LoadCommentMultiple(...) — Aggregates comment across tracks (read-only). 6/10
•	LoadLyrics(...) — Loads editable lyrics TagItem for one track. 7/10
•	LoadLyricsMultiple(...) — Aggregates lyrics across tracks (read-only). 6/10
•	GetLyricsValue(...) — Reads lyrics from known ATL additional-field keys. 6/10
•	SetLyricsValue(...) — Writes/removes lyrics in additional fields. 6/10
•	CreatePlaceholderComment() — Produces no-comment placeholder item. 3/10
•	CreatePlaceholderLyrics() — Produces no-lyrics placeholder item. 3/10

ViewModels/Properties/Loaders/PictureInfoLoaderAtl.cs
•	Load(...) — Loads single-track cover metadata/details and editable description. 8/10
•	LoadMultiple(...) — Aggregates picture metadata across selected tracks. 8/10
•	ComputeSimpleHash(byte[]) — Lightweight hash for cover-content sameness checks. 5/10
•	AddPictureInfoItem(...) — Helper to append picture metadata items. 4/10

ViewModels/Properties/Loaders/ReplayGainLoaderAtl.cs
•	Load(...) — Loads ReplayGain tags for one track with edit capability checks. 8/10
•	LoadMultiple(...) — Aggregates ReplayGain tags across tracks with safe edit gating. 8/10
•	GetReplayGainField(...) — Reads ReplayGain value from additional fields. 6/10
•	SetReplayGainField(...) — Writes/removes ReplayGain field in additional fields. 6/10
•	IsReplayGainEditable(...) — Determines ReplayGain write support by format. 6/10
•	AddReplayGainItem(...) — Helper to append ReplayGain TagItem rows. 4/10