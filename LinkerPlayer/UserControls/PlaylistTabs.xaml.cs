using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using LinkerPlayer.Windows;
using ManagedBass;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LinkerPlayer.UserControls;

[ObservableObject]
public partial class PlaylistTabs
{
    private EditableTabHeaderControl? _selectedEditableTabHeaderControl;
    private readonly ILogger<PlaylistTabs> _logger;


    public PlaylistTabs()
    {
        InitializeComponent();

        _logger = App.AppHost.Services.GetRequiredService<ILogger<PlaylistTabs>>();

        Loaded += PlaylistTabs_Loaded;
    }

    private void PlaylistTabs_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlaylistTabsViewModel viewModel)
        {
            viewModel.LoadPlaylistTabs();
            _logger.LogInformation("PlaylistTabs: Loaded {Count} playlists", viewModel.TabList.Count);
            if (viewModel.TabList.Any())
            {
                viewModel.SelectedTabIndex = 0; // Force initial selection
                _logger.LogInformation("PlaylistTabs: Set SelectedTabIndex to 0");
            }
            else
            {
                _logger.LogWarning("PlaylistTabs: No playlists loaded");
            }
        }
        else
        {
            _logger.LogError("PlaylistTabs: DataContext is not PlaylistTabsViewModel, type: {Type}", DataContext?.GetType().FullName ?? "null");
        }
    }

    private void DataGrid_Loaded(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            if (DataContext is PlaylistTabsViewModel viewModel)
            {
                viewModel.OnDataGridLoaded(sender, e);
            }
        }, null);
    }

    private void TracksTable_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            if (DataContext is PlaylistTabsViewModel viewModel)
            {
                viewModel.OnTrackSelectionChanged(sender, e);
            }
        }, null);
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl && (e.AddedItems.Count > 0 || e.RemovedItems.Count > 0))
        {
            bool isTabChange = e.AddedItems.OfType<PlaylistTab>().Any() || e.RemovedItems.OfType<PlaylistTab>().Any();
            if (isTabChange)
            {
                this.Dispatcher.BeginInvoke((Action)delegate
                {
                    if (DataContext is PlaylistTabsViewModel viewModel)
                    {
                        viewModel.OnTabSelectionChanged(sender, e);
                    }
                }, null);
                Console.WriteLine("Tab selection changed.");
            }
        }
        else
        {
            Console.WriteLine($"Event from child control {e.OriginalSource}, ignoring.");
        }
    }

    private void PlaylistRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            if (DataContext is PlaylistTabsViewModel viewModel)
            {
                viewModel.OnDoubleClickDataGrid();
            }
        }, null);

        WeakReferenceMessenger.Default.Send(new DataGridPlayMessage(PlaybackState.Playing));
    }

    private void MenuItem_NewPlaylist(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke(async () =>
        {
            if (DataContext is PlaylistTabsViewModel viewModel)
            {
                await viewModel.NewPlaylistAsync();
            }
        });
    }

    private void MenuItem_LoadPlaylistAsync(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke(async () =>
        {
            if (DataContext is PlaylistTabsViewModel viewModel)
            {
                await viewModel.LoadPlaylistAsync();
            }
        });
    }

    private void MenuItem_RenamePlaylist(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            if (_selectedEditableTabHeaderControl != null)
            {
                _selectedEditableTabHeaderControl.SetEditMode(true);
                // Pass PlaylistTab to enable command binding
                if (_selectedEditableTabHeaderControl.DataContext is PlaylistTab && DataContext is PlaylistTabsViewModel viewModel)
                {
                    _selectedEditableTabHeaderControl.Tag = viewModel; // Store view model for command access
                }
            }
        }, null);
    }

    private void MenuItem_RemovePlaylist(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke(async () =>
        {
            if (DataContext is PlaylistTabsViewModel viewModel)
            {
                await viewModel.RemovePlaylistAsync(sender);
            }
        });
    }

    private void MenuItem_AddFolder(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke(async () =>
        {
            if (DataContext is PlaylistTabsViewModel viewModel)
            {
                await viewModel.AddFolderAsync();
            }
        });
    }

    private void MenuItem_AddFiles(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke(async () =>
        {
            if (DataContext is PlaylistTabsViewModel viewModel)
            {
                await viewModel.AddFilesAsync();
            }
        });
    }

    private void MenuItem_PlayTrack(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            if (DataContext is PlayerControlsViewModel viewModel)
            {
                viewModel.PlayTrack();
            }
        }, null);
    }

    private void MenuItem_RemoveTrack(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke(async () =>
        {
            if (DataContext is PlaylistTabsViewModel viewModel)
            {
                await viewModel.RemoveTrackAsync();
            }
        });
    }

    private void MenuItem_NewPlaylistFromFolder(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke(async () =>
        {
            if (DataContext is PlaylistTabsViewModel viewModel)
            {
                await viewModel.NewPlaylistFromFolderAsync();
            }
        });
    }

    private void MenuItem_Properties(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlaylistTabsViewModel playlistTabsViewModel)
        {
            var sharedDataModel = playlistTabsViewModel.SharedDataModel;
            var logger = App.AppHost.Services.GetRequiredService<ILogger<PropertiesViewModel>>();
            var propertiesViewModel = new PropertiesViewModel(sharedDataModel, logger);
            var dialog = new PropertiesWindow
            {
                DataContext = propertiesViewModel
            };
            dialog.Show();
        }
    }

    private void TabHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            _selectedEditableTabHeaderControl = (EditableTabHeaderControl)sender;
        }, null);
    }

    private void TabHeader_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _selectedEditableTabHeaderControl = (EditableTabHeaderControl)sender;
        if (DataContext is PlaylistTabsViewModel viewModel)
        {
            viewModel.RightMouseDownTabSelect((string)_selectedEditableTabHeaderControl.Content);
        }
    }

    private void TracksTable_OnSorting(object sender, DataGridSortingEventArgs e)
    {
        if (e.Column is DataGridColumn column)
        {
            ListSortDirection direction = (column.SortDirection != ListSortDirection.Ascending)
                ? ListSortDirection.Ascending
                : ListSortDirection.Descending;
            string propertyName = (column.SortMemberPath ?? column.Header.ToString())!;
            this.Dispatcher.BeginInvoke((Action)delegate
            {
                if (DataContext is PlaylistTabsViewModel viewModel)
                {
                    viewModel.OnDataGridSorted(propertyName, direction);
                }
            }, null);
        }
    }
}