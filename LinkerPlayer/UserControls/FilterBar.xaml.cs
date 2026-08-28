using LinkerPlayer.Core;
using LinkerPlayer.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

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
        if (DataContext is not LibraryTab libraryTab)
        {
            return;
        }

        // Wire once for this control instance to avoid duplicate subscriptions/refresh cascades.
        this.Loaded -= FilterBar_Loaded;

        // Apply initial selections from viewmodel to the ListBoxes after bindings are settled
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                ApplyAllSelections(libraryTab);
            }
            catch { }
        }), DispatcherPriority.Background);

        // After every facet rebuild, re-apply ListBox selections from VM state.
        // This is required because WPF ListBox.SelectedItems is not bindable.
        libraryTab.FacetsRebuilt += (_, __) =>
        {
            try
            {
                ApplyAllSelections(libraryTab);
            }
            catch { }
        };

        // Persist keyword/query search changes
        libraryTab.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LibraryTab.KeywordSearch))
            {
                SaveFilterSelections(libraryTab);
            }
        };

        // Persist explicit filter row changes (add/remove/edit)
        libraryTab.ActiveFilters.CollectionChanged += (_, __) => SaveFilterSelections(libraryTab);
    }

    /// <summary>
    /// Reapplies all four listbox selections from the VM state in a single suppressed batch.
    /// </summary>
    private void ApplyAllSelections(LibraryTab libraryTab)
    {
        _suppressSelectionChanged = true;
        try
        {
            ApplySelectionsToListBox(GenresListBox, libraryTab.SelectedGenres);
            ApplySelectionsToListBox(ArtistsListBox, libraryTab.SelectedArtists);
            ApplySelectionsToListBox(AlbumsListBox, libraryTab.SelectedAlbums);
            ApplySelectionsToListBox(CodecsListBox, libraryTab.SelectedCodecs);
            EnsureAllSelectedFallback(GenresListBox, libraryTab.SelectedGenres);
            EnsureAllSelectedFallback(ArtistsListBox, libraryTab.SelectedArtists);
            EnsureAllSelectedFallback(AlbumsListBox, libraryTab.SelectedAlbums);
            EnsureAllSelectedFallback(CodecsListBox, libraryTab.SelectedCodecs);
        }
        finally
        {
            _suppressSelectionChanged = false;
        }
    }

    private void ApplySelectionsToListBox(ListBox listBox, ObservableCollection<string> selected)
    {
        if (listBox == null || selected == null)
            return;

        // Snapshot the selected collection to avoid "Collection was modified" if the VM updates selections
        List<string> snapshot = new List<string>(selected);

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

    private void ClearGenre_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryTab libraryTab)
        {
            libraryTab.SelectedGenres.Clear();
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => libraryTab.NotifyGenresChanged()), DispatcherPriority.Background);
        }
    }


    private void CodecsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged)
            return;

        if (DataContext is not LibraryTab libraryTab)
            return;
        if (sender is not ListBox lb)
            return;

        libraryTab.SelectedCodecs.Clear();
        foreach (object item in lb.SelectedItems)
        {
            if (item is string s)
                libraryTab.SelectedCodecs.Add(s);
        }

        SaveFilterSelections(libraryTab);

        _suppressSelectionChanged = true;
        try
        {
            libraryTab.NotifyCodecsChanged();
        }
        finally
        {
            _suppressSelectionChanged = false;
        }
    }

    private void GenresList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged)
            return;

        if (DataContext is not LibraryTab libraryTab)
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

        _suppressSelectionChanged = true;
        try
        {
            libraryTab.NotifyGenresChanged();
        }
        finally
        {
            _suppressSelectionChanged = false;
        }
    }

    private void ArtistsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged)
            return;

        if (DataContext is not LibraryTab libraryTab)
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

        _suppressSelectionChanged = true;
        try
        {
            libraryTab.NotifyArtistsChanged();
        }
        finally
        {
            _suppressSelectionChanged = false;
        }
    }

    private void AlbumsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged)
            return;

        if (DataContext is not LibraryTab libraryTab)
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

        _suppressSelectionChanged = true;
        try
        {
            libraryTab.NotifyAlbumsChanged();
        }
        finally
        {
            _suppressSelectionChanged = false;
        }
    }

    private void SaveFilterSelections(LibraryTab libraryTab)
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
            settingsManager.Settings.LastLibrarySelectedCodecs = new List<string>(libraryTab.SelectedCodecs);

            settingsManager.Settings.LastLibraryKeywordSearch = libraryTab.KeywordSearch;

            settingsManager.Settings.LastLibraryActiveFilters = libraryTab.ActiveFilters
                .Select(f => new AppSettings.FilterCriteriaSettings
                {
                    Type = f.Type.ToString(),
                    Operator = f.Operator.ToString(),
                    Value = f.Value,
                    ValueSecondary = f.ValueSecondary,
                    IsEnabled = f.IsEnabled
                })
                .ToList();

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
        if (DataContext is not LibraryTab libraryTab)
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

        if (DataContext is not LibraryTab libraryTab)
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
        if (DataContext is not LibraryTab libraryTab)
        {
            return;
        }

        libraryTab.ClearAllFilters();
    }

    private void QueryHelpButton_Click(object sender, RoutedEventArgs e)
    {
        QueryHelpPopup.IsOpen = !QueryHelpPopup.IsOpen;
    }
}
