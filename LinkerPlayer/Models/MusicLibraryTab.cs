using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using System.Linq;

namespace LinkerPlayer.Models;

/// <summary>
/// Represents the Music Library tab - a special permanent tab that displays all tracks in the library.
/// Unlike PlaylistTab, this tab cannot be renamed, moved, or deleted.
/// </summary>
public partial class MusicLibraryTab : ObservableObject, ITabData
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

    // Reference to the main library collection (exposed via Tracks property)
    private readonly ObservableCollection<MediaFile> _sourceLibrary;

    // Debounce timer — coalesces rapid CollectionChanged bursts (e.g. during import)
    // into a single RebuildMetadataLists + RefreshView call.
    private readonly DispatcherTimer _rebuildDebounceTimer;

    // Filtered/sorted view of the library
    [ObservableProperty]
    private CollectionViewSource? _tracksView;

    // Current selection
    [ObservableProperty]
    private MediaFile? _selectedTrack;

    [ObservableProperty]
    private int? _selectedIndex;

    // Filter state
    [ObservableProperty]
    private ObservableCollection<FilterCriteria> _activeFilters = new();

    [ObservableProperty]
    private string _keywordSearch = string.Empty;

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
        "Genre"
    };

    public MusicLibraryTab(ObservableCollection<MediaFile> sourceLibrary)
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

        // Subscribe to filter collection changes to refresh view
        ActiveFilters.CollectionChanged += (_, __) => RefreshView();

        // Debounce timer: fires once after 500 ms of quiet, coalescing rapid import batches
        _rebuildDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _rebuildDebounceTimer.Tick += (_, __) =>
        {
            _rebuildDebounceTimer.Stop();
            RebuildMetadataLists();
            RefreshView();
        };

        // Selection changes are driven by the UI and handled explicitly to avoid re-entrancy

        // Subscribe to source collection changes — debounce so import batches don't thrash the UI
        _sourceLibrary.CollectionChanged += (_, __) => ScheduleRebuild();

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

        // Ensure view reflects initial selections
        NotifyGenresChanged();
        NotifyArtistsChanged();
        NotifyAlbumsChanged();
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
    }

    // Selection change handling is managed via collection change events for multi-select selections.

    /// <summary>
    /// Public entry point for UI to notify that multi-select selections changed.
    /// Call this after updating SelectedGenres/SelectedArtists/SelectedAlbums to refresh dependent lists and view.
    /// </summary>
    public void NotifySelectionsChanged()
    {
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

        // Notify to refresh dependent lists and view
        NotifyGenresChanged();
        NotifyArtistsChanged();
        NotifyAlbumsChanged();
        NotifyCodecsChanged();
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
            NotifyGenresChanged();
        }

        if (SelectedArtists.Count == 0 && Artists.Contains("(All)"))
        {
            SelectedArtists.Add("(All)");
            NotifyArtistsChanged();
        }

        if (SelectedAlbums.Count == 0 && Albums.Contains("(All)"))
        {
            SelectedAlbums.Add("(All)");
            NotifyAlbumsChanged();
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

    /// <summary>
    /// Rebuilds all four facet listboxes symmetrically so each one shows only values
    /// that appear in tracks matching all the *other* three active filters.
    /// </summary>
    private void UpdateAllFacetLists()
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
        if (SelectedGenres == null || SelectedGenres.Count == 0 || SelectedGenres.Contains("(All)", StringComparer.OrdinalIgnoreCase))
            return source;

        HashSet<string> sel = new(SelectedGenres, StringComparer.OrdinalIgnoreCase);

        return source.Where(t =>
        {
            if (string.IsNullOrWhiteSpace(t.Genres))
                return false;

            string[] tokens = t.Genres!.Split(new[] { '/', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);

            return tokens.Select(x => x.Trim()).Any(tok => sel.Contains(tok));
        });
    }

    private IEnumerable<MediaFile> ApplyArtistFilter(IEnumerable<MediaFile> source)
    {
        if (SelectedArtists == null || SelectedArtists.Count == 0 || SelectedArtists.Contains("(All)", StringComparer.OrdinalIgnoreCase))
            return source;

        HashSet<string> sel = new(SelectedArtists, StringComparer.OrdinalIgnoreCase);

        return source.Where(t => !string.IsNullOrWhiteSpace(t.Artist) && sel.Contains(t.Artist));
    }

    private IEnumerable<MediaFile> ApplyAlbumFilter(IEnumerable<MediaFile> source)
    {
        if (SelectedAlbums == null || SelectedAlbums.Count == 0 || SelectedAlbums.Contains("(All)", StringComparer.OrdinalIgnoreCase))
            return source;

        HashSet<string> sel = new(SelectedAlbums, StringComparer.OrdinalIgnoreCase);

        return source.Where(t => !string.IsNullOrWhiteSpace(t.Album) && sel.Contains(t.Album));
    }

    private IEnumerable<MediaFile> ApplyCodecFilter(IEnumerable<MediaFile> source)
    {
        if (SelectedCodecs == null || SelectedCodecs.Count == 0 || SelectedCodecs.Contains("(All)", StringComparer.OrdinalIgnoreCase))
            return source;

        HashSet<string> sel = new(SelectedCodecs, StringComparer.OrdinalIgnoreCase);

        return source.Where(t => !string.IsNullOrWhiteSpace(t.Codec) && sel.Contains(t.Codec));
    }

    public void NotifyGenresChanged()
    {
        UpdateAllFacetLists();
        RefreshView();
    }

    public void NotifyArtistsChanged()
    {
        UpdateAllFacetLists();
        RefreshView();
    }

    public void NotifyAlbumsChanged()
    {
        UpdateAllFacetLists();
        RefreshView();
    }

    public void NotifyCodecsChanged()
    {
        UpdateAllFacetLists();
        RefreshView();
    }

    /// <summary>
    /// Applies all active filters to a track
    /// </summary>
    private bool ApplyFilters(object item)
    {
        if (item is not MediaFile track)
        {
            return false;
        }

        // Apply keyword search first (searches across all metadata)
        if (!string.IsNullOrWhiteSpace(KeywordSearch))
        {
            string keyword = KeywordSearch.ToLowerInvariant();
            bool matchesKeyword =
                track.Title?.ToLowerInvariant().Contains(keyword) == true ||
                track.Artist?.ToLowerInvariant().Contains(keyword) == true ||
                track.Album?.ToLowerInvariant().Contains(keyword) == true ||
                track.AlbumArtist?.ToLowerInvariant().Contains(keyword) == true ||
                track.Genres?.ToLowerInvariant().Contains(keyword) == true ||
                track.Performers?.ToLowerInvariant().Contains(keyword) == true ||
                track.Composers?.ToLowerInvariant().Contains(keyword) == true ||
                track.Comment?.ToLowerInvariant().Contains(keyword) == true ||
                track.Copyright?.ToLowerInvariant().Contains(keyword) == true ||
                track.Codec?.ToLowerInvariant().Contains(keyword) == true ||
                track.FileName?.ToLowerInvariant().Contains(keyword) == true ||
                track.Path?.ToLowerInvariant().Contains(keyword) == true;

            if (!matchesKeyword)
            {
                return false;
            }
        }

        // Apply each active filter (all must match - AND logic)
        foreach (FilterCriteria filter in ActiveFilters.Where(f => f.IsEnabled))
        {
            if (!filter.Matches(track))
            {
                return false;
            }
        }

        // Apply multi-select metadata filters
        // Genres
        if (SelectedGenres != null && SelectedGenres.Count > 0 && !SelectedGenres.Contains("(All)", StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(track.Genres))
                return false;
            string[] tokens = track.Genres!.Split(new[] { '/', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            HashSet<string> sel = new(SelectedGenres, StringComparer.OrdinalIgnoreCase);
            if (!tokens.Select(t => t.Trim()).Any(tok => sel.Contains(tok)))
                return false;
        }

        // Artists
        if (SelectedArtists != null && SelectedArtists.Count > 0 && !SelectedArtists.Contains("(All)", StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(track.Artist))
                return false;
            HashSet<string> sel = new(SelectedArtists, StringComparer.OrdinalIgnoreCase);
            if (!sel.Contains(track.Artist))
                return false;
        }

        // Albums
        if (SelectedAlbums != null && SelectedAlbums.Count > 0 && !SelectedAlbums.Contains("(All)", StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(track.Album))
                return false;
            HashSet<string> sel = new(SelectedAlbums, StringComparer.OrdinalIgnoreCase);
            if (!sel.Contains(track.Album))
                return false;
        }

        // Codecs
        if (SelectedCodecs != null && SelectedCodecs.Count > 0 && !SelectedCodecs.Contains("(All)", StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(track.Codec))
                return false;
            HashSet<string> sel = new(SelectedCodecs, StringComparer.OrdinalIgnoreCase);
            if (!sel.Contains(track.Codec))
                return false;
        }

        return true;
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

        // Set callback so filter changes trigger view refresh
        filter.OnFilterChanged = RefreshView;

        ActiveFilters.Add(filter);
        RefreshView();
    }

    /// <summary>
    /// Removes a filter and refreshes the view
    /// </summary>
    public void RemoveFilter(FilterCriteria filter)
    {
        if (filter != null && ActiveFilters.Remove(filter))
        {
            RefreshView();
        }
    }

    /// <summary>
    /// Clears all filters and refreshes the view
    /// </summary>
    public void ClearAllFilters()
    {
        ActiveFilters.Clear();
        KeywordSearch = string.Empty;
        RefreshView();
    }

    partial void OnKeywordSearchChanged(string value)
    {
        // Refresh view when keyword search changes
        RefreshView();
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
            FilterOperator.Contains => MatchesContains(trackValue),
            FilterOperator.GreaterThan => MatchesGreaterThan(track),
            FilterOperator.LessThan => MatchesLessThan(track),
            FilterOperator.Between => MatchesBetween(track),
            _ => true
        };
    }

    private string? GetTrackValue(MediaFile track)
    {
        return Type switch
        {
            FilterType.Artist => track.Artist,
            FilterType.Album => track.Album,
            FilterType.Genre => track.Genres,
            FilterType.Year => track.Year.ToString(),
            FilterType.Bitrate => track.Bitrate.ToString(),
            FilterType.FileType => System.IO.Path.GetExtension(track.Path),
            FilterType.Performer => track.Performers,
            FilterType.Composer => track.Composers,
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
    Artist,
    Album,
    Genre,
    Year,
    Bitrate,
    FileType,
    Performer,
    Composer
}

/// <summary>
/// Filter comparison operators
/// </summary>
public enum FilterOperator
{
    Equals,
    Contains,
    GreaterThan,
    LessThan,
    Between
}
