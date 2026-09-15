using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace LinkerPlayer.Models;

/// <summary>
/// Represents the Music Library tab - a special permanent tab that displays all tracks in the library.
/// Unlike PlaylistTab, this tab cannot be renamed, moved, or deleted.
/// </summary>
public partial class LibraryTab : ObservableObject, ITabData
{
    // Display name for the library (not editable)
    public string Name { get; } = "Music Library";

    /// <summary>
    /// Raised after all four facet listbox collections and their SelectedXxx collections
    /// have been fully rebuilt.  The code-behind uses this to reapply ListBox.SelectedItems
    /// in one suppressed batch, avoiding the re-entrancy loop that occurs when individual
    /// CollectionChanged events fire mid-rebuild.
    /// </summary>
    public event EventHandler? FacetsRebuilt;

    /// <summary>
    /// Raised after the filtered CollectionView is refreshed (filter/sort change).
    /// Subscribers should re-apply DataGrid.SelectedItem to survive the CollectionView Reset.
    /// </summary>
    public event EventHandler? ViewRefreshed;

    // Reference to the main library collection (exposed via Tracks property)
    private readonly ObservableCollection<MediaFile> _sourceLibrary;

    // Debounce timer — coalesces rapid CollectionChanged bursts (e.g. during import)
    // into a single RebuildMetadataLists + RefreshView call.
    private readonly DispatcherTimer _rebuildDebounceTimer;

    // Debounce timer for keyword text changes so rapid typing does not refresh the view on every keystroke.
    private readonly DispatcherTimer _keywordRefreshDebounceTimer;

    // Filtered/sorted view of the library
    [ObservableProperty]
    private CollectionViewSource? _tracksView;

    // Active multi-column sort applied as a pre-sorted snapshot (not via view.SortDescriptions,
    // which would force the view to re-sort/re-filter on every item PropertyChanged during scroll).
    private List<SortDescription>? _activeSorts;

    // Current selection
    [ObservableProperty]
    private MediaFile? _selectedTrack;

    [ObservableProperty]
    private int? _selectedIndex;

    public MediaFile? SelectedMediaFile
    {
        get => SelectedTrack;
        set => SelectedTrack = value;
    }

    partial void OnSelectedTrackChanged(MediaFile? value)
    {
        OnPropertyChanged(nameof(SelectedMediaFile));
    }

    // Filter state
    [ObservableProperty]
    private ObservableCollection<FilterCriteria> _activeFilters = new();

    [ObservableProperty]
    private string _keywordSearch = string.Empty;

    private bool _hasKeywordFilter;
    private bool _keywordIsStructuredQuery;
    private string _plainKeywordSearch = string.Empty;
    private List<List<FilterCriteria>>? _keywordOrGroups;

    private bool _hasGenreSelectionFilter;
    private bool _hasArtistSelectionFilter;
    private bool _hasAlbumSelectionFilter;
    private bool _hasCodecSelectionFilter;
    private HashSet<string>? _selectedGenreSet;
    private HashSet<string>? _selectedArtistSet;
    private HashSet<string>? _selectedAlbumSet;
    private HashSet<string>? _selectedCodecSet;

    private FilterCriteria[] _enabledFilters = [];

    // Library-specific column configuration
    [ObservableProperty]
    private List<string> _visibleColumns = new()
    {
        "Playing",
        "Title",
        "Artist",
        "Album",
        "Year",
        "Duration",
        "Bitrate",
        "ReplayGain",
        "Genre"
    };

    private bool _isUpdatingFacets;

    public LibraryTab(ObservableCollection<MediaFile> sourceLibrary)
    {
        _sourceLibrary = sourceLibrary ?? throw new ArgumentNullException(nameof(sourceLibrary));

        // Initialize the CollectionViewSource for filtering/sorting
        TracksView = new CollectionViewSource
        {
            Source = _sourceLibrary
        };

        // Set up live filtering
        if (TracksView.View != null)
        {
            TracksView.View.Filter = ApplyFilters;
        }

        // ActiveFilters changes are applied through explicit filter commands/handlers.
        // Avoid extra CollectionChanged-driven refresh churn during startup restore.

        // Debounce timer: fires once after 500 ms of quiet, coalescing rapid import batches
        _rebuildDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _rebuildDebounceTimer.Tick += (_, __) =>
        {
            _rebuildDebounceTimer.Stop();
            RebuildMetadataLists();
            NotifyFilteredTrackCountChanged();
        };

        _keywordRefreshDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _keywordRefreshDebounceTimer.Tick += (_, __) =>
        {
            _keywordRefreshDebounceTimer.Stop();
            RefreshView();
        };

        // Selection changes are driven by the UI and handled explicitly to avoid re-entrancy

        // Subscribe to source collection changes — debounce so import batches don't thrash the UI
        _sourceLibrary.CollectionChanged += (_, __) =>
        {
            ScheduleRebuild();
            NotifyFilteredTrackCountChanged();

            // If a sorted snapshot is active, re-sort to pick up newly imported tracks.
            if (_activeSorts is { Count: > 0 })
            {
                ApplySortedSnapshot();
            }
        };

        // Build metadata lists (genres, artists, albums) for the filter bar
        RebuildMetadataLists();

        // Default initial selection: show All in each list so UI matches initial state
        if (SelectedGenres.Count == 0 && Genres.Contains("(All)"))
        {
            SelectedGenres.Add("(All)");
        }

        if (SelectedArtists.Count == 0 && Artists.Contains("(All)"))
        {
            SelectedArtists.Add("(All)");
        }

        if (SelectedAlbums.Count == 0 && Albums.Contains("(All)"))
        {
            SelectedAlbums.Add("(All)");
        }

        if (SelectedCodecs.Count == 0 && Codecs.Contains("(All)"))
        {
            SelectedCodecs.Add("(All)");
        }

        RebuildFacetSelectionCaches();
        RebuildEnabledFiltersCache();
    }

    /// <summary>
    /// Gets the tracks collection (direct access to source library for ITabData compatibility)
    /// For filtered view, UI should bind to TracksView.View instead
    /// </summary>
    public ObservableCollection<MediaFile> Tracks => _sourceLibrary;

    /// <summary>
    /// Gets the count of filtered tracks
    /// </summary>
    public int FilteredTrackCount => TracksView?.View?.Cast<MediaFile>().Count() ?? 0;

    // Metadata lists for FilterBar (Genres, Artists, Albums)
    [ObservableProperty]
    private ObservableCollection<string> _genres = new();

    [ObservableProperty]
    private ObservableCollection<string> _artists = new();

    [ObservableProperty]
    private ObservableCollection<string> _albums = new();

    private ObservableCollection<string> _codecs = new();
    public ObservableCollection<string> Codecs => _codecs;

    // Multi-select selections (allow multiple genres/artists/albums)
    private readonly ObservableCollection<string> _selectedGenres = new();
    public ObservableCollection<string> SelectedGenres => _selectedGenres;

    private readonly ObservableCollection<string> _selectedArtists = new();
    public ObservableCollection<string> SelectedArtists => _selectedArtists;

    private readonly ObservableCollection<string> _selectedAlbums = new();
    public ObservableCollection<string> SelectedAlbums => _selectedAlbums;

    private readonly ObservableCollection<string> _selectedCodecs = new();
    public ObservableCollection<string> SelectedCodecs => _selectedCodecs;

    /// <summary>
    /// Gets the count of genres (excluding the "(All)" item)
    /// </summary>
    public int GenreCount => Math.Max(0, Genres?.Count - 1 ?? 0);

    /// <summary>
    /// Gets the count of artists (excluding the "(All)" item)
    /// </summary>
    public int ArtistCount => Math.Max(0, Artists?.Count - 1 ?? 0);

    /// <summary>
    /// Gets the count of albums (excluding the "(All)" item)
    /// </summary>
    public int AlbumCount => Math.Max(0, Albums?.Count - 1 ?? 0);

    /// <summary>
    /// Gets the count of codecs (excluding the "(All)" item)
    /// </summary>
    public int CodecCount => Math.Max(0, Codecs.Count - 1);

    /// <summary>
    /// Refreshes the filtered view
    /// </summary>
    public void RefreshView()
    {
        // Ensure CollectionViewSource.View.Refresh runs on the UI thread (Dispatcher-bound)
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IEditableCollectionView? editableInner = TracksView?.View as IEditableCollectionView;
                if (editableInner != null && (editableInner.IsAddingNew || editableInner.IsEditingItem))
                {
                    return;
                }

                TracksView?.View?.Refresh();
                OnPropertyChanged(nameof(FilteredTrackCount));
                ViewRefreshed?.Invoke(this, EventArgs.Empty);
            });
            return;
        }

        IEditableCollectionView? editable = TracksView?.View as IEditableCollectionView;
        if (editable != null && (editable.IsAddingNew || editable.IsEditingItem))
        {
            // Defer refresh until the edit transaction completes
            return;
        }

        TracksView?.View?.Refresh();
        OnPropertyChanged(nameof(FilteredTrackCount));
        ViewRefreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Applies a multi-column sort by reordering a detached snapshot and using it as the
    /// view's source with SortDescriptions cleared. This avoids the ListCollectionView's
    /// live re-sort/re-filter on every MediaFile PropertyChanged, which was the source of
    /// scroll jank on large libraries. Pass an empty/null list to restore the live source.
    /// </summary>
    public void SetSortOrder(IReadOnlyList<SortDescription>? sorts)
    {
        _activeSorts = sorts is { Count: > 0 } ? new List<SortDescription>(sorts) : null;

        if (TracksView?.View == null)
            return;

        // The view must not also carry SortDescriptions, or it re-sorts live during scroll.
        if (TracksView.View.SortDescriptions.Count > 0)
        {
            TracksView.View.SortDescriptions.Clear();
        }

        if (_activeSorts == null)
        {
            // Restore the live source when sorting is cleared.
            if (!ReferenceEquals(TracksView.Source, _sourceLibrary))
            {
                TracksView.Source = _sourceLibrary;
            }
        }
        else
        {
            ApplySortedSnapshot();
        }

        RefreshView();
    }

    /// <summary>
    /// The currently applied multi-column sort, in priority order. Empty when unsorted.
    /// </summary>
    public IReadOnlyList<SortDescription> ActiveSorts =>
        _activeSorts is { Count: > 0 } ? _activeSorts : (IReadOnlyList<SortDescription>)Array.Empty<SortDescription>();

    private void ApplySortedSnapshot()
    {
        if (TracksView == null || _activeSorts == null || _activeSorts.Count == 0)
            return;

        IOrderedEnumerable<MediaFile> ordered = BuildOrderedQuery(_sourceLibrary);
        TracksView.Source = ordered.ToList();
    }

    private IOrderedEnumerable<MediaFile> BuildOrderedQuery(IEnumerable<MediaFile> source)
    {
        IOrderedEnumerable<MediaFile>? ordered = null;
        foreach (SortDescription sort in _activeSorts!)
        {
            Func<MediaFile, object?> key = KeySelectorFor(sort.PropertyName);
            bool desc = sort.Direction == ListSortDirection.Descending;

            ordered = ordered == null
                ? (desc ? source.OrderByDescending(key) : source.OrderBy(key))
                : (desc ? ordered.ThenByDescending(key) : ordered.ThenBy(key));
        }
        return ordered!;
    }

    // Strongly-typed key selectors avoid reflection on every comparison during the sort.
    private static Func<MediaFile, object?> KeySelectorFor(string propertyName) => propertyName switch
    {
        nameof(MediaFile.Title) => t => t.Title,
        nameof(MediaFile.Artist) => t => t.Artist,
        nameof(MediaFile.Album) => t => t.Album,
        nameof(MediaFile.AlbumArtist) => t => t.AlbumArtist,
        nameof(MediaFile.Genres) => t => t.Genres,
        nameof(MediaFile.Codec) => t => t.Codec,
        nameof(MediaFile.Track) => t => t.Track,
        nameof(MediaFile.TrackCount) => t => t.TrackCount,
        nameof(MediaFile.Disc) => t => t.Disc,
        nameof(MediaFile.DiscCount) => t => t.DiscCount,
        nameof(MediaFile.Year) => t => t.Year,
        nameof(MediaFile.Duration) => t => t.Duration,
        nameof(MediaFile.Bitrate) => t => t.Bitrate,
        nameof(MediaFile.SampleRate) => t => t.SampleRate,
        nameof(MediaFile.Channels) => t => t.Channels,
        nameof(MediaFile.Rating) => t => t.Rating,
        nameof(MediaFile.FileName) => t => t.FileName,
        nameof(MediaFile.Path) => t => t.Path,
        nameof(MediaFile.Composers) => t => t.Composers,
        nameof(MediaFile.Comment) => t => t.Comment,
        nameof(MediaFile.Copyright) => t => t.Copyright,
        nameof(MediaFile.ReplayGain) => t => t.ReplayGain,
        // Fallback for any unmapped property: reflect once per item rather than per comparison.
        _ => CreateReflectionSelector(propertyName)
    };

    private static Func<MediaFile, object?> CreateReflectionSelector(string propertyName)
    {
        System.Reflection.PropertyInfo? prop = typeof(MediaFile).GetProperty(propertyName);
        return prop == null ? (Func<MediaFile, object?>)(_ => null) : (t => prop.GetValue(t));
    }

    /// <summary>
    /// Public entry point for UI to notify that multi-select selections changed.
    /// Call this after updating SelectedGenres/SelectedArtists/SelectedAlbums to refresh dependent lists and view.
    /// </summary>
    public void NotifySelectionsChanged()
    {
        RebuildFacetSelectionCaches();
        UpdateAllFacetLists();
        RefreshView();
    }

    /// <summary>
    /// Restores filter selections from app settings.
    /// Call this after the metadata lists are built to restore user's last session state.
    /// </summary>
    public void RestoreFilterSelections(Models.AppSettings settings)
    {
        if (settings == null)
        {
            return;
        }

        // Clear current selections
        SelectedGenres.Clear();
        SelectedArtists.Clear();
        SelectedAlbums.Clear();
        SelectedCodecs.Clear();

        // Restore genres
        if (settings.LastLibrarySelectedGenres != null && settings.LastLibrarySelectedGenres.Count > 0)
        {
            foreach (string genre in settings.LastLibrarySelectedGenres)
            {
                if (Genres.Contains(genre, StringComparer.OrdinalIgnoreCase))
                {
                    SelectedGenres.Add(genre);
                }
            }
        }

        // Restore artists
        if (settings.LastLibrarySelectedArtists != null && settings.LastLibrarySelectedArtists.Count > 0)
        {
            foreach (string artist in settings.LastLibrarySelectedArtists)
            {
                if (Artists.Contains(artist, StringComparer.OrdinalIgnoreCase))
                {
                    SelectedArtists.Add(artist);
                }
            }
        }

        // Restore albums
        if (settings.LastLibrarySelectedAlbums != null && settings.LastLibrarySelectedAlbums.Count > 0)
        {
            foreach (string album in settings.LastLibrarySelectedAlbums)
            {
                if (Albums.Contains(album, StringComparer.OrdinalIgnoreCase))
                {
                    SelectedAlbums.Add(album);
                }
            }
        }

        // If nothing was restored, default to "(All)"
        if (SelectedGenres.Count == 0 && Genres.Contains("(All)"))
        {
            SelectedGenres.Add("(All)");
        }

        if (SelectedArtists.Count == 0 && Artists.Contains("(All)"))
        {
            SelectedArtists.Add("(All)");
        }

        if (SelectedAlbums.Count == 0 && Albums.Contains("(All)"))
        {
            SelectedAlbums.Add("(All)");
        }

        if (settings.LastLibrarySelectedCodecs != null && settings.LastLibrarySelectedCodecs.Count > 0)
        {
            foreach (string codec in settings.LastLibrarySelectedCodecs)
            {
                if (Codecs.Contains(codec, StringComparer.OrdinalIgnoreCase))
                {
                    SelectedCodecs.Add(codec);
                }
            }
        }

        if (SelectedCodecs.Count == 0 && Codecs.Contains("(All)"))
        {
            SelectedCodecs.Add("(All)");
        }

        // Restore keyword/query search
        KeywordSearch = settings.LastLibraryKeywordSearch ?? string.Empty;

        // Restore explicit filter rows
        if (settings.LastLibraryActiveFilters != null)
        {
            ActiveFilters.Clear();
            foreach (AppSettings.FilterCriteriaSettings dto in settings.LastLibraryActiveFilters)
            {
                if (!Enum.TryParse(dto.Type, out FilterType filterType))
                {
                    continue;
                }

                if (!Enum.TryParse(dto.Operator, out FilterOperator filterOp))
                {
                    continue;
                }

                FilterCriteria fc = new FilterCriteria
                {
                    Type = filterType,
                    Operator = filterOp,
                    Value = dto.Value,
                    ValueSecondary = dto.ValueSecondary,
                    IsEnabled = dto.IsEnabled,
                    OnFilterChanged = OnActiveFilterChanged
                };
                ActiveFilters.Add(fc);
            }
        }

        RebuildFacetSelectionCaches();
        RebuildEnabledFiltersCache();
    }

    /// <summary>
    /// Rebuilds the master metadata lists from the source library.
    /// </summary>
    private void RebuildMetadataLists()
    {
        // Ensure this runs on the UI thread because we update ObservableCollections
        // that are bound to CollectionViewSource.Source (Dispatcher-bound).
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => RebuildMetadataLists());
            return;
        }
        HashSet<string> genreSet = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> artistSet = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> albumSet = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> codecSet = new(StringComparer.OrdinalIgnoreCase);

        // Take a resilient snapshot of the source collection to avoid "collection modified" during enumeration
        List<MediaFile> sourceSnapshot = _sourceLibrary.ToList();

        foreach (MediaFile track in sourceSnapshot)
        {
            if (!string.IsNullOrWhiteSpace(track.Genres))
            {
                // Split multi-genre tokens (common delimiters)
                string[] tokens = track.Genres!.Split(new[] { '/', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string token in tokens)
                {
                    string t = token.Trim();
                    if (!string.IsNullOrEmpty(t))
                    {
                        genreSet.Add(t);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(track.Artist))
            {
                artistSet.Add(track.Artist!);
            }

            if (!string.IsNullOrWhiteSpace(track.Album))
            {
                albumSet.Add(track.Album!);
            }

            if (!string.IsNullOrWhiteSpace(track.Codec))
            {
                codecSet.Add(track.Codec!);
            }
        }

        List<string> genresList = genreSet.OrderBy(s => s).ToList();
        List<string> artistsList = artistSet.OrderBy(s => s).ToList();
        List<string> albumsList = albumSet.OrderBy(s => s).ToList();
        List<string> codecsList = codecSet.OrderBy(s => s).ToList();

        // Insert a top-level "(All)" option (do not auto-select)
        genresList.Insert(0, "(All)");
        artistsList.Insert(0, "(All)");
        albumsList.Insert(0, "(All)");
        codecsList.Insert(0, "(All)");

        // Update existing collections in-place to preserve bindings
        if (Genres == null)
        {
            Genres = new ObservableCollection<string>(genresList);
        }
        else
        {
            Genres.Clear();
            foreach (string g in genresList)
            {
                Genres.Add(g);
            }
        }

        if (Artists == null)
        {
            Artists = new ObservableCollection<string>(artistsList);
        }
        else
        {
            Artists.Clear();
            foreach (string a in artistsList)
            {
                Artists.Add(a);
            }
        }

        if (Albums == null)
        {
            Albums = new ObservableCollection<string>(albumsList);
        }
        else
        {
            Albums.Clear();
            foreach (string al in albumsList)
            {
                Albums.Add(al);
            }
        }

        Codecs.Clear();
        foreach (string c in codecsList)
        {
            Codecs.Add(c);
        }

        // Ensure selected entries remain present; remove any selections that are no longer valid
        RemoveInvalidSelections(SelectedGenres, Genres);
        RemoveInvalidSelections(SelectedArtists, Artists);
        RemoveInvalidSelections(SelectedAlbums, Albums);
        RemoveInvalidSelections(SelectedCodecs, Codecs);

        // Raise property changed for counts
        OnPropertyChanged(nameof(GenreCount));
        OnPropertyChanged(nameof(ArtistCount));
        OnPropertyChanged(nameof(AlbumCount));
        OnPropertyChanged(nameof(CodecCount));

        // Subscribe to property changes on items so metadata updates when a track's tags change
        SubscribeToMediaFilePropertyChanges();

        // Ensure default initial selection remains when metadata rebuilds later
        if (SelectedGenres.Count == 0 && Genres.Contains("(All)"))
        {
            SelectedGenres.Add("(All)");
        }

        if (SelectedArtists.Count == 0 && Artists.Contains("(All)"))
        {
            SelectedArtists.Add("(All)");
        }

        if (SelectedAlbums.Count == 0 && Albums.Contains("(All)"))
        {
            SelectedAlbums.Add("(All)");
        }

        if (SelectedCodecs.Count == 0 && Codecs.Contains("(All)"))
        {
            SelectedCodecs.Add("(All)");
        }
    }

    private void RemoveInvalidSelections(ObservableCollection<string> selected, ObservableCollection<string> available)
    {
        if (selected == null || available == null)
        {
            return;
        }

        for (int i = selected.Count - 1; i >= 0; i--)
        {
            string s = selected[i];
            if (!available.Contains(s, StringComparer.OrdinalIgnoreCase))
            {
                selected.RemoveAt(i);
            }
        }
    }

    private void SubscribeToMediaFilePropertyChanges()
    {
        // Detach existing handlers first using a snapshot to avoid collection-modified exceptions
        List<MediaFile> snapshot = _sourceLibrary.ToList();
        foreach (MediaFile item in snapshot)
        {
            item.PropertyChanged -= MediaFile_PropertyChanged;
        }

        // Re-attach handlers using a fresh snapshot in case the source changed while detaching
        snapshot = _sourceLibrary.ToList();
        foreach (MediaFile item in snapshot)
        {
            item.PropertyChanged += MediaFile_PropertyChanged;
        }
    }

    private void MediaFile_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // If relevant tag fields changed, schedule a debounced rebuild
        if (e.PropertyName == nameof(MediaFile.Genres) || e.PropertyName == nameof(MediaFile.Artist) || e.PropertyName == nameof(MediaFile.Album))
        {
            ScheduleRebuild();
        }
    }

    /// <summary>
    /// Schedules a debounced rebuild. Resets the 500 ms window on each call so that
    /// rapid-fire changes (import batches, background metadata refresh) collapse into one rebuild.
    /// Safe to call from any thread — marshals to the UI dispatcher.
    /// </summary>
    private void ScheduleRebuild()
    {
        if (Application.Current == null)
            return;

        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke(ScheduleRebuild, DispatcherPriority.Background);
            return;
        }

        // Reset the timer — each new change extends the quiet window
        _rebuildDebounceTimer.Stop();
        _rebuildDebounceTimer.Start();
    }

    private void NotifyFilteredTrackCountChanged()
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke(
                () => OnPropertyChanged(nameof(FilteredTrackCount)),
                DispatcherPriority.Background);
            return;
        }

        OnPropertyChanged(nameof(FilteredTrackCount));
    }

    /// <summary>
    /// Rebuilds all four facet listboxes symmetrically so each one shows only values
    /// that appear in tracks matching all the *other* three active filters.
    /// </summary>
    private void UpdateAllFacetLists()
    {
        if (_isUpdatingFacets)
            return;

        _isUpdatingFacets = true;

        try
        {
            List<MediaFile> all = _sourceLibrary.ToList();

            // Build a filtered base for each dimension by applying the OTHER three filters
            List<MediaFile> ForGenres()
            {
                IEnumerable<MediaFile> q = all;
                q = ApplyArtistFilter(ApplyAlbumFilter(ApplyCodecFilter(q)));
                return q.ToList();
            }

            List<MediaFile> ForArtists()
            {
                IEnumerable<MediaFile> q = all;
                q = ApplyGenreFilter(ApplyAlbumFilter(ApplyCodecFilter(q)));
                return q.ToList();
            }

            List<MediaFile> ForAlbums()
            {
                IEnumerable<MediaFile> q = all;
                q = ApplyGenreFilter(ApplyArtistFilter(ApplyCodecFilter(q)));
                return q.ToList();
            }

            List<MediaFile> ForCodecs()
            {
                IEnumerable<MediaFile> q = all;
                q = ApplyGenreFilter(ApplyArtistFilter(ApplyAlbumFilter(q)));
                return q.ToList();
            }

            RebuildFacet(Genres, SelectedGenres, ForGenres(), t =>
            {
                if (string.IsNullOrWhiteSpace(t.Genres))
                    return Enumerable.Empty<string>();
                return t.Genres!.Split(new[] { '/', ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s));
            });

            RebuildFacet(Artists, SelectedArtists, ForArtists(), t =>
                string.IsNullOrWhiteSpace(t.Artist) ? Enumerable.Empty<string>() : new[] { t.Artist! });

            RebuildFacet(Albums, SelectedAlbums, ForAlbums(), t =>
                string.IsNullOrWhiteSpace(t.Album) ? Enumerable.Empty<string>() : new[] { t.Album! });

            RebuildFacet(Codecs, SelectedCodecs, ForCodecs(), t =>
                string.IsNullOrWhiteSpace(t.Codec) ? Enumerable.Empty<string>() : new[] { t.Codec! });

            OnPropertyChanged(nameof(GenreCount));
            OnPropertyChanged(nameof(ArtistCount));
            OnPropertyChanged(nameof(AlbumCount));
            OnPropertyChanged(nameof(CodecCount));

            FacetsRebuilt?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _isUpdatingFacets = false;
        }
    }

    private static void RebuildFacet(
        ObservableCollection<string> list,
        ObservableCollection<string> selected,
        List<MediaFile> tracks,
        Func<MediaFile, IEnumerable<string>> valueSelector)
    {
        HashSet<string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (MediaFile t in tracks)
        {
            foreach (string v in valueSelector(t))
            {
                values.Add(v);
            }
        }

        list.Clear();
        list.Add("(All)");
        foreach (string v in values.OrderBy(s => s))
        {
            list.Add(v);
        }

        // Drop any selected items that no longer exist in the refreshed list
        for (int i = selected.Count - 1; i >= 0; i--)
        {
            if (!list.Contains(selected[i], StringComparer.OrdinalIgnoreCase))
            {
                selected.RemoveAt(i);
            }
        }
    }

    // --- Reusable filter predicates (each honours its own dimension's selection) ---

    private IEnumerable<MediaFile> ApplyGenreFilter(IEnumerable<MediaFile> source)
    {
        if (!_hasGenreSelectionFilter || _selectedGenreSet == null)
            return source;

        return source.Where(t => HasAnyMatchingGenre(t.Genres, _selectedGenreSet));
    }

    private IEnumerable<MediaFile> ApplyArtistFilter(IEnumerable<MediaFile> source)
    {
        if (!_hasArtistSelectionFilter || _selectedArtistSet == null)
            return source;

        return source.Where(t => !string.IsNullOrWhiteSpace(t.Artist) && _selectedArtistSet.Contains(t.Artist));
    }

    private IEnumerable<MediaFile> ApplyAlbumFilter(IEnumerable<MediaFile> source)
    {
        if (!_hasAlbumSelectionFilter || _selectedAlbumSet == null)
            return source;

        return source.Where(t => !string.IsNullOrWhiteSpace(t.Album) && _selectedAlbumSet.Contains(t.Album));
    }

    private IEnumerable<MediaFile> ApplyCodecFilter(IEnumerable<MediaFile> source)
    {
        if (!_hasCodecSelectionFilter || _selectedCodecSet == null)
            return source;

        return source.Where(t => !string.IsNullOrWhiteSpace(t.Codec) && _selectedCodecSet.Contains(t.Codec));
    }

    public void NotifyGenresChanged() { if (!_isUpdatingFacets) { RebuildFacetSelectionCaches(); UpdateAllFacetLists(); RefreshView(); } }
    public void NotifyArtistsChanged() { if (!_isUpdatingFacets) { RebuildFacetSelectionCaches(); UpdateAllFacetLists(); RefreshView(); } }
    public void NotifyAlbumsChanged() { if (!_isUpdatingFacets) { RebuildFacetSelectionCaches(); UpdateAllFacetLists(); RefreshView(); } }
    public void NotifyCodecsChanged() { if (!_isUpdatingFacets) { RebuildFacetSelectionCaches(); UpdateAllFacetLists(); RefreshView(); } }

    /// <summary>
    /// Applies all active filters to a track
    /// </summary>
    private bool ApplyFilters(object item)
    {
        if (item is not MediaFile track)
        {
            return false;
        }

        // Apply keyword search first — use cached parse state to avoid per-item query parsing.
        if (_hasKeywordFilter)
        {
            if (_keywordIsStructuredQuery)
            {
                List<List<FilterCriteria>>? orGroups = _keywordOrGroups;
                if (orGroups == null || orGroups.Count == 0)
                {
                    return false;
                }

                bool matchesQuery = false;
                foreach (List<FilterCriteria> andGroup in orGroups)
                {
                    bool groupMatches = true;
                    foreach (FilterCriteria criterion in andGroup)
                    {
                        if (!criterion.Matches(track))
                        {
                            groupMatches = false;
                            break;
                        }
                    }

                    if (groupMatches)
                    {
                        matchesQuery = true;
                        break;
                    }
                }

                if (!matchesQuery)
                {
                    return false;
                }
            }
            else if (!MatchesAnyMetadataField(track, _plainKeywordSearch))
            {
                return false;
            }
        }

        // Apply each active filter (all must match - AND logic)
        foreach (FilterCriteria filter in _enabledFilters)
        {
            if (!filter.Matches(track))
            {
                return false;
            }
        }

        // Apply multi-select metadata filters
        if (_hasGenreSelectionFilter && (_selectedGenreSet == null || !HasAnyMatchingGenre(track.Genres, _selectedGenreSet)))
        {
            return false;
        }

        if (_hasArtistSelectionFilter && (_selectedArtistSet == null || string.IsNullOrWhiteSpace(track.Artist) || !_selectedArtistSet.Contains(track.Artist)))
        {
            return false;
        }

        if (_hasAlbumSelectionFilter && (_selectedAlbumSet == null || string.IsNullOrWhiteSpace(track.Album) || !_selectedAlbumSet.Contains(track.Album)))
        {
            return false;
        }

        if (_hasCodecSelectionFilter && (_selectedCodecSet == null || string.IsNullOrWhiteSpace(track.Codec) || !_selectedCodecSet.Contains(track.Codec)))
        {
            return false;
        }

        return true;
    }

    private static bool HasAnyMatchingGenre(string? genres, HashSet<string> selectedGenres)
    {
        if (string.IsNullOrWhiteSpace(genres))
        {
            return false;
        }

        string[] tokens = genres.Split(['/', ';', ','], StringSplitOptions.RemoveEmptyEntries);
        foreach (string token in tokens)
        {
            if (selectedGenres.Contains(token.Trim()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesAnyMetadataField(MediaFile track, string keyword)
    {
        return Contains(track.Title, keyword)
               || Contains(track.Artist, keyword)
               || Contains(track.Album, keyword)
               || Contains(track.AlbumArtist, keyword)
               || Contains(track.Performers, keyword)
               || Contains(track.Composers, keyword)
               || Contains(track.Genres, keyword)
               || Contains(track.Comment, keyword)
               || Contains(track.Copyright, keyword)
               || Contains(track.Codec, keyword)
               || Contains(track.ReplayGain, keyword)
               || Contains(track.FileName, keyword)
               || Contains(track.Path, keyword)
               || Contains(track.Track.ToString(), keyword)
               || Contains(track.TrackCount.ToString(), keyword)
               || Contains(track.Disc.ToString(), keyword)
               || Contains(track.DiscCount.ToString(), keyword)
               || Contains(track.Year.ToString(), keyword)
               || Contains(track.Duration.ToString(), keyword)
               || Contains(track.Bitrate.ToString(), keyword)
               || Contains(track.SampleRate.ToString(), keyword)
               || Contains(track.Channels.ToString(), keyword)
               || Contains(track.Rating.ToString(), keyword)
               || Contains(track.LeadingSilenceMs?.ToString(), keyword)
               || Contains(track.TrailingSilenceMs?.ToString(), keyword)
               || Contains(track.Source.ToString(), keyword)
               || Contains(track.WatchedFolderPath, keyword)
               || Contains(track.HealthStatus.ToString(), keyword);
    }

    private static bool Contains(string? value, string keyword)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildFacetSelectionCaches()
    {
        (_hasGenreSelectionFilter, _selectedGenreSet) = BuildSelectionSet(SelectedGenres);
        (_hasArtistSelectionFilter, _selectedArtistSet) = BuildSelectionSet(SelectedArtists);
        (_hasAlbumSelectionFilter, _selectedAlbumSet) = BuildSelectionSet(SelectedAlbums);
        (_hasCodecSelectionFilter, _selectedCodecSet) = BuildSelectionSet(SelectedCodecs);
    }

    private static (bool HasFilter, HashSet<string>? Set) BuildSelectionSet(ObservableCollection<string> selected)
    {
        if (selected.Count == 0 || selected.Contains("(All)", StringComparer.OrdinalIgnoreCase))
        {
            return (false, null);
        }

        return (true, new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase));
    }

    private void RebuildEnabledFiltersCache()
    {
        _enabledFilters = ActiveFilters.Where(f => f.IsEnabled).ToArray();
    }

    private void OnActiveFilterChanged()
    {
        RebuildEnabledFiltersCache();
        RefreshView();
    }

    private void RebuildKeywordFilterCache(string? keywordSearch)
    {
        string keyword = keywordSearch?.Trim() ?? string.Empty;
        _hasKeywordFilter = keyword.Length > 0;

        if (!_hasKeywordFilter)
        {
            _keywordIsStructuredQuery = false;
            _plainKeywordSearch = string.Empty;
            _keywordOrGroups = null;
            return;
        }

        if (QueryParser.TryParse(keyword, out List<List<FilterCriteria>> orGroups))
        {
            _keywordIsStructuredQuery = true;
            _keywordOrGroups = orGroups;
            _plainKeywordSearch = string.Empty;
            return;
        }

        _keywordIsStructuredQuery = false;
        _keywordOrGroups = null;
        _plainKeywordSearch = keyword;
    }

    private void ScheduleKeywordRefresh()
    {
        if (Application.Current == null)
        {
            return;
        }

        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke(ScheduleKeywordRefresh, DispatcherPriority.Background);
            return;
        }

        _keywordRefreshDebounceTimer.Stop();
        _keywordRefreshDebounceTimer.Start();
    }

    /// <summary>
    /// Adds a new filter and refreshes the view
    /// </summary>
    public void AddFilter(FilterCriteria filter)
    {
        if (filter == null)
        {
            throw new ArgumentNullException(nameof(filter));
        }

        // Set callback so filter changes trigger cache rebuild and view refresh
        filter.OnFilterChanged = OnActiveFilterChanged;

        ActiveFilters.Add(filter);
        RebuildEnabledFiltersCache();
        RefreshView();
    }

    /// <summary>
    /// Removes a filter and refreshes the view
    /// </summary>
    public void RemoveFilter(FilterCriteria filter)
    {
        if (filter != null && ActiveFilters.Remove(filter))
        {
            RebuildEnabledFiltersCache();
            RefreshView();
        }
    }

    /// <summary>
    /// Clears all filters and refreshes the view
    /// </summary>
    public void ClearAllFilters()
    {
        ActiveFilters.Clear();
        RebuildEnabledFiltersCache();

        if (string.IsNullOrWhiteSpace(KeywordSearch))
        {
            RebuildKeywordFilterCache(string.Empty);
            RefreshView();
            return;
        }

        KeywordSearch = string.Empty;
    }

    partial void OnKeywordSearchChanged(string value)
    {
        RebuildKeywordFilterCache(value);
        ScheduleKeywordRefresh();
    }
}

/// <summary>
/// Represents a single filter criterion
/// </summary>
public partial class FilterCriteria : ObservableObject
{
    [ObservableProperty]
    private FilterType _type = FilterType.None;

    [ObservableProperty]
    private FilterOperator _operator = FilterOperator.Equals;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private string? _valueSecondary; // For range operations

    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>
    /// Action called when filter criteria changes (to trigger view refresh)
    /// </summary>
    public Action? OnFilterChanged { get; set; }

    partial void OnTypeChanged(FilterType value) => OnFilterChanged?.Invoke();
    partial void OnOperatorChanged(FilterOperator value) => OnFilterChanged?.Invoke();
    partial void OnValueChanged(string value) => OnFilterChanged?.Invoke();
    partial void OnValueSecondaryChanged(string? value) => OnFilterChanged?.Invoke();
    partial void OnIsEnabledChanged(bool value) => OnFilterChanged?.Invoke();

    /// <summary>
    /// Tests if a track matches this filter criterion
    /// </summary>
    public bool Matches(MediaFile track)
    {
        if (!IsEnabled || Type == FilterType.None)
        {
            return true;
        }

        string? trackValue = GetTrackValue(track);
        if (trackValue == null && Type != FilterType.Year && Type != FilterType.Bitrate)
        {
            return false;
        }

        return Operator switch
        {
            FilterOperator.Equals => MatchesEquals(trackValue),
            FilterOperator.NotEquals => !MatchesEquals(trackValue),
            FilterOperator.Contains => MatchesContains(trackValue),
            FilterOperator.GreaterThan => MatchesGreaterThan(track),
            FilterOperator.GreaterThanOrEqual => MatchesGreaterThanOrEqual(track),
            FilterOperator.LessThan => MatchesLessThan(track),
            FilterOperator.LessThanOrEqual => MatchesLessThanOrEqual(track),
            FilterOperator.Between => MatchesBetween(track),
            _ => true
        };
    }

    private string? GetTrackValue(MediaFile track)
    {
        return Type switch
        {
            FilterType.Title => track.Title,
            FilterType.Artist => track.Artist,
            FilterType.Album => track.Album,
            FilterType.Genre => track.Genres,
            FilterType.Year => track.Year.ToString(),
            FilterType.Bitrate => track.Bitrate.ToString(),
            FilterType.FileType => System.IO.Path.GetExtension(track.Path),
            FilterType.Performer => track.Performers,
            FilterType.Composer => track.Composers,
            FilterType.ReplayGain => track.ReplayGain,
            _ => null
        };
    }

    private bool MatchesEquals(string? trackValue)
    {
        if (trackValue == null)
        {
            return false;
        }

        return trackValue.Equals(Value, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesContains(string? trackValue)
    {
        if (trackValue == null)
        {
            return false;
        }

        return trackValue.Contains(Value, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesGreaterThan(MediaFile track)
    {
        if (Type == FilterType.Year && uint.TryParse(Value, out uint yearValue))
        {
            return track.Year > yearValue;
        }

        if (Type == FilterType.Bitrate && int.TryParse(Value, out int bitrateValue))
        {
            return track.Bitrate > bitrateValue;
        }

        return false;
    }

    private bool MatchesLessThan(MediaFile track)
    {
        if (Type == FilterType.Year && uint.TryParse(Value, out uint yearValue))
        {
            return track.Year < yearValue;
        }

        if (Type == FilterType.Bitrate && int.TryParse(Value, out int bitrateValue))
        {
            return track.Bitrate < bitrateValue;
        }

        return false;
    }

    private bool MatchesGreaterThanOrEqual(MediaFile track)
    {
        if (Type == FilterType.Year && uint.TryParse(Value, out uint yearValue))
        {
            return track.Year >= yearValue;
        }

        if (Type == FilterType.Bitrate && int.TryParse(Value, out int bitrateValue))
        {
            return track.Bitrate >= bitrateValue;
        }

        return false;
    }

    private bool MatchesLessThanOrEqual(MediaFile track)
    {
        if (Type == FilterType.Year && uint.TryParse(Value, out uint yearValue))
        {
            return track.Year <= yearValue;
        }

        if (Type == FilterType.Bitrate && int.TryParse(Value, out int bitrateValue))
        {
            return track.Bitrate <= bitrateValue;
        }

        return false;
    }

    private bool MatchesBetween(MediaFile track)
    {
        if (string.IsNullOrWhiteSpace(ValueSecondary))
        {
            return false;
        }

        if (Type == FilterType.Year &&
            uint.TryParse(Value, out uint yearMin) &&
            uint.TryParse(ValueSecondary, out uint yearMax))
        {
            return track.Year >= yearMin && track.Year <= yearMax;
        }

        if (Type == FilterType.Bitrate &&
            int.TryParse(Value, out int bitrateMin) &&
            int.TryParse(ValueSecondary, out int bitrateMax))
        {
            return track.Bitrate >= bitrateMin && track.Bitrate <= bitrateMax;
        }

        return false;
    }
}

/// <summary>
/// Types of filters available
/// </summary>
public enum FilterType
{
    None,
    Title,
    Artist,
    Album,
    Genre,
    Year,
    Bitrate,
    FileType,
    Performer,
    Composer,
    ReplayGain
}

/// <summary>
/// Filter comparison operators
/// </summary>
public enum FilterOperator
{
    Equals,
    NotEquals,
    Contains,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Between
}
