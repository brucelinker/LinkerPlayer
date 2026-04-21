using LinkerPlayer.Models;
using LinkerPlayer.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;

namespace LinkerPlayer.UserControls;

/// <summary>
/// Filter bar control for the Music Library tab.
/// Provides keyword search and multiple metadata filters.
/// </summary>
public partial class FilterBar : UserControl
{
    public FilterBar()
    {
        InitializeComponent();
        this.Loaded += FilterBar_Loaded;
    }

    // When we programmatically set ListBox.SelectedItems we must suppress the SelectionChanged handlers
    // to avoid clearing the ViewModel collections during initialization.
    private bool _suppressSelectionChanged;

    private void EnsureAllSelectedFallback(ListBox listBox, System.Collections.ObjectModel.ObservableCollection<string> selected)
    {
        if (listBox == null || selected == null)
            return;

        if (listBox.SelectedItems.Count == 0)
        {
            // Try to find the "(All)" item in the Items collection (case-insensitive)
            object? allItem = null;
            for (int i = 0; i < listBox.Items.Count; i++)
            {
                if (string.Equals(listBox.Items[i] as string, "(All)", StringComparison.OrdinalIgnoreCase))
                {
                    allItem = listBox.Items[i];
                    break;
                }
            }

            if (allItem != null)
            {
                try
                {
                    listBox.SelectedItems.Add(allItem);
                }
                catch { }
            }
        }
    }

    private void FilterBar_Loaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MusicLibraryTab libraryTab)
        {
            return;
        }

        // Apply initial selections from viewmodel to the ListBoxes after bindings are settled
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                ApplySelectionsToListBox(GenresListBox, libraryTab.SelectedGenres);
                ApplySelectionsToListBox(ArtistsListBox, libraryTab.SelectedArtists);
                ApplySelectionsToListBox(AlbumsListBox, libraryTab.SelectedAlbums);
                // Ensure each listbox has a default selection of "(All)" when none selected yet
                EnsureAllSelectedFallback(GenresListBox, libraryTab.SelectedGenres);
                EnsureAllSelectedFallback(ArtistsListBox, libraryTab.SelectedArtists);
                EnsureAllSelectedFallback(AlbumsListBox, libraryTab.SelectedAlbums);
                // Subscribe to collection changes so we reapply selections when ItemsSource updates
                libraryTab.Genres.CollectionChanged += (_, __) => Application.Current?.Dispatcher.BeginInvoke(new Action(() => ApplySelectionsToListBox(GenresListBox, libraryTab.SelectedGenres)), DispatcherPriority.Background);
                libraryTab.Artists.CollectionChanged += (_, __) => Application.Current?.Dispatcher.BeginInvoke(new Action(() => ApplySelectionsToListBox(ArtistsListBox, libraryTab.SelectedArtists)), DispatcherPriority.Background);
                libraryTab.Albums.CollectionChanged += (_, __) => Application.Current?.Dispatcher.BeginInvoke(new Action(() => ApplySelectionsToListBox(AlbumsListBox, libraryTab.SelectedAlbums)), DispatcherPriority.Background);
            }
            catch { }
        }), DispatcherPriority.Background);
    }

    private void ApplySelectionsToListBox(ListBox listBox, ObservableCollection<string> selected)
    {
        if (listBox == null || selected == null)
            return;

        // Snapshot the selected collection to avoid "Collection was modified" if the VM updates selections
        List<string> snapshot = new List<string>(selected);

        try
        {
            _suppressSelectionChanged = true;
            listBox.SelectedItems.Clear();
            foreach (string s in snapshot)
            {
                for (int i = 0; i < listBox.Items.Count; i++)
                {
                    if (string.Equals(listBox.Items[i] as string, s, StringComparison.OrdinalIgnoreCase))
                    {
                        listBox.SelectedItems.Add(listBox.Items[i]);
                        break;
                    }
                }
            }
        }
        finally
        {
            _suppressSelectionChanged = false;
        }
    }

    private void ClearGenre_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MusicLibraryTab libraryTab)
        {
            libraryTab.SelectedGenres.Clear();
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => libraryTab.NotifyGenresChanged()), DispatcherPriority.Background);
        }
    }


    private void GenresList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged)
            return;

        if (DataContext is not MusicLibraryTab libraryTab)
            return;
        if (sender is not ListBox lb)
            return;
        libraryTab.SelectedGenres.Clear();
        foreach (object item in lb.SelectedItems)
        {
            if (item is string s)
            {
                libraryTab.SelectedGenres.Add(s);
            }
        }

        // Persist selection to settings
        SaveFilterSelections(libraryTab);

        // Notify once after batch update to avoid re-entrancy
        // Defer to the dispatcher so ListBox selection processing completes before we mutate ItemsSources
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => libraryTab.NotifyGenresChanged()), DispatcherPriority.Background);
    }

    private void ArtistsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged)
            return;

        if (DataContext is not MusicLibraryTab libraryTab)
            return;
        if (sender is not ListBox lb)
            return;
        libraryTab.SelectedArtists.Clear();
        foreach (object item in lb.SelectedItems)
        {
            if (item is string s)
            {
                libraryTab.SelectedArtists.Add(s);
            }
        }

        // Persist selection to settings
        SaveFilterSelections(libraryTab);

        Application.Current?.Dispatcher.BeginInvoke(new Action(() => libraryTab.NotifyArtistsChanged()), DispatcherPriority.Background);
    }

    private void AlbumsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged)
            return;

        if (DataContext is not MusicLibraryTab libraryTab)
            return;
        if (sender is not ListBox lb)
            return;
        libraryTab.SelectedAlbums.Clear();
        foreach (object item in lb.SelectedItems)
        {
            if (item is string s)
            {
                libraryTab.SelectedAlbums.Add(s);
            }
        }

        // Persist selection to settings
        SaveFilterSelections(libraryTab);

        Application.Current?.Dispatcher.BeginInvoke(new Action(() => libraryTab.NotifyAlbumsChanged()), DispatcherPriority.Background);
    }

    private void SaveFilterSelections(MusicLibraryTab libraryTab)
    {
        try
        {
            ISettingsManager? settingsManager = App.AppHost?.Services.GetService<ISettingsManager>();
            if (settingsManager is null)
            {
                return;
            }

            settingsManager.Settings.LastLibrarySelectedGenres = new List<string>(libraryTab.SelectedGenres);
            settingsManager.Settings.LastLibrarySelectedArtists = new List<string>(libraryTab.SelectedArtists);
            settingsManager.Settings.LastLibrarySelectedAlbums = new List<string>(libraryTab.SelectedAlbums);

            settingsManager.SaveSettings(nameof(AppSettings.LastLibrarySelectedGenres));
        }
        catch
        {
            // Ignore settings save failures
        }
    }

    /// <summary>
    /// Adds a new filter row (max 4 filters)
    /// </summary>
    private void AddFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MusicLibraryTab libraryTab)
        {
            return;
        }

        // Limit to 4 active filters
        if (libraryTab.ActiveFilters.Count >= 4)
        {
            MessageBox.Show(
                "Maximum of 4 filters allowed. Please remove an existing filter first.",
                "Filter Limit Reached",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Add a new empty filter
        FilterCriteria newFilter = new FilterCriteria
        {
            Type = FilterType.None,
            Operator = FilterOperator.Equals,
            Value = string.Empty,
            IsEnabled = true
        };

        libraryTab.AddFilter(newFilter);
    }

    /// <summary>
    /// Removes the specified filter
    /// </summary>
    private void RemoveFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not FilterCriteria filter)
        {
            return;
        }

        if (DataContext is not MusicLibraryTab libraryTab)
        {
            return;
        }

        libraryTab.RemoveFilter(filter);
    }

    /// <summary>
    /// Clears all filters and keyword search
    /// </summary>
    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MusicLibraryTab libraryTab)
        {
            return;
        }

        libraryTab.ClearAllFilters();
    }
}
