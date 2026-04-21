using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Linq;
using System.Threading;

namespace LinkerPlayer.Models;

/// <summary>
/// Represents the Music Library tab - a special permanent tab that displays all tracks in the library.
/// Unlike PlaylistTab, this tab cannot be renamed, moved, or deleted.
/// </summary>
public partial class MusicLibraryTab : ObservableObject, ITabData
{
    // Display name for the library (not editable)
    public string Name { get; } = "Music Library";

    // Reference to the main library collection (exposed via Tracks property)
    private readonly ObservableCollection<MediaFile> _sourceLibrary;

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

        // Selection changes are driven by the UI and handled explicitly to avoid re-entrancy

        // Subscribe to source collection changes and item property changes so metadata lists stay current
        _sourceLibrary.CollectionChanged += (_, __) => 
        {
            RebuildMetadataLists();
            RefreshView();
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

    // Multi-select selections (allow multiple genres/artists/albums)
    private readonly ObservableCollection<string> _selectedGenres = new();
    public ObservableCollection<string> SelectedGenres => _selectedGenres;

    private readonly ObservableCollection<string> _selectedArtists = new();
    public ObservableCollection<string> SelectedArtists => _selectedArtists;

    private readonly ObservableCollection<string> _selectedAlbums = new();
    public ObservableCollection<string> SelectedAlbums => _selectedAlbums;

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
        UpdateDependentMetadataLists();
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

        // Notify to refresh dependent lists and view
        NotifyGenresChanged();
        NotifyArtistsChanged();
        NotifyAlbumsChanged();
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
        }

        List<string> genresList = genreSet.OrderBy(s => s).ToList();
        List<string> artistsList = artistSet.OrderBy(s => s).ToList();
        List<string> albumsList = albumSet.OrderBy(s => s).ToList();

        // Insert a top-level "(All)" option (do not auto-select)
        genresList.Insert(0, "(All)");
        artistsList.Insert(0, "(All)");
        albumsList.Insert(0, "(All)");

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

        // Ensure selected entries remain present; remove any selections that are no longer valid
        RemoveInvalidSelections(SelectedGenres, Genres);
        RemoveInvalidSelections(SelectedArtists, Artists);
        RemoveInvalidSelections(SelectedAlbums, Albums);

        // Raise property changed for counts
        OnPropertyChanged(nameof(GenreCount));
        OnPropertyChanged(nameof(ArtistCount));
        OnPropertyChanged(nameof(AlbumCount));

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
        // If relevant tag fields changed, rebuild metadata lists
        if (e.PropertyName == nameof(MediaFile.Genres) || e.PropertyName == nameof(MediaFile.Artist) || e.PropertyName == nameof(MediaFile.Album))
        {
            RebuildMetadataLists();
            RefreshView();
        }
    }

    /// <summary>
    /// Updates dependent metadata lists (Artists and Albums) based on current selections.
    /// </summary>
    private void UpdateDependentMetadataLists()
    {
        IEnumerable<MediaFile> filtered = _sourceLibrary;

        // Apply selected genres if any (and not selecting the special "(All)" token)
        if (SelectedGenres != null && SelectedGenres.Count > 0 && !SelectedGenres.Contains("(All)", StringComparer.OrdinalIgnoreCase))
        {
            HashSet<string> sel = new(SelectedGenres, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(t =>
            {
                if (string.IsNullOrWhiteSpace(t.Genres))
                    return false;
                string[] tokens = t.Genres!.Split(new[] { '/', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                return tokens.Select(x => x.Trim()).Any(tok => sel.Contains(tok));
            });
        }

        // Materialize the filtered collection to avoid enumeration issues
        List<MediaFile> filteredList = filtered.ToList();

        // Build artists list from filtered set
        IEnumerable<string> artists = filteredList.Select(t => t.Artist).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s!);
        Artists.Clear();
        Artists.Add("(All)");
        foreach (string a in artists)
            Artists.Add(a);

        // Apply selected artist filter if present
        if (SelectedArtists != null && SelectedArtists.Count > 0 && !SelectedArtists.Contains("(All)", StringComparer.OrdinalIgnoreCase))
        {
            HashSet<string> selA = new(SelectedArtists, StringComparer.OrdinalIgnoreCase);
            filteredList = filteredList.Where(t => !string.IsNullOrWhiteSpace(t.Artist) && selA.Contains(t.Artist)).ToList();
        }

        // Build albums list from further filtered set
        List<string> albums = filteredList.Select(t => t.Album)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s!)
            .ToList();

        Albums.Clear();
        Albums.Add("(All)");
        foreach (string al in albums)
            Albums.Add(al);

        // Raise property changed for counts
        OnPropertyChanged(nameof(ArtistCount));
        OnPropertyChanged(nameof(AlbumCount));
    }

    /// <summary>
    /// Update only the Albums collection based on current SelectedGenres and SelectedArtists.
    /// Used when the Artist selection changes so we don't rebuild the Artists collection and disturb its selection.
    /// </summary>
    public void UpdateAlbumsOnly()
    {
        IEnumerable<MediaFile> filtered = _sourceLibrary;

        if (SelectedGenres != null && SelectedGenres.Count > 0 && !SelectedGenres.Contains("(All)", StringComparer.OrdinalIgnoreCase))
        {
            HashSet<string> sel = new(SelectedGenres, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(t =>
            {
                if (string.IsNullOrWhiteSpace(t.Genres))
                    return false;
                string[] tokens = t.Genres!.Split(new[] { '/', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                return tokens.Select(x => x.Trim()).Any(tok => sel.Contains(tok));
            });
        }

        if (SelectedArtists != null && SelectedArtists.Count > 0 && !SelectedArtists.Contains("(All)", StringComparer.OrdinalIgnoreCase))
        {
            HashSet<string> selA = new(SelectedArtists, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(t => !string.IsNullOrWhiteSpace(t.Artist) && selA.Contains(t.Artist));
        }

        IEnumerable<string> albums = filtered.Select(t => t.Album).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s!);

        Albums.Clear();
        Albums.Add("(All)");
        foreach (string al in albums)
            Albums.Add(al);

        RemoveInvalidSelections(SelectedAlbums, Albums);
    }

    public void NotifyGenresChanged()
    {
        UpdateDependentMetadataLists();
        RefreshView();
    }

    public void NotifyArtistsChanged()
    {
        UpdateAlbumsOnly();
        RefreshView();
    }

    public void NotifyAlbumsChanged()
    {
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
