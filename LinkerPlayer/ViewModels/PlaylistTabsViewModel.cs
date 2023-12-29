using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using Microsoft.Win32;
using Serilog;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LinkerPlayer.ViewModels;

public partial class PlaylistTabsViewModel : ObservableObject
{
    [ObservableProperty]
    private string? _selectedPlaylistName;

    [ObservableProperty]
    private static MediaFile? _selectedTrack;

    [ObservableProperty]
    private static PlaylistTab? _selectedTab;

    [ObservableProperty] 
    private static PlayerState _state;

    public static ObservableCollection<PlaylistTab> TabList { get; set; } = new();

    public void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedTrack = (sender as DataGrid)!.SelectedItem as MediaFile;

        WeakReferenceMessenger.Default.Send(new PlaylistSelectionChangedMessage(SelectedTrack));
    }

    public void UpdatePlayerState(PlayerState state)
    {
        State = state;
        if (SelectedTrack != null) SelectedTrack.State = state;
    }

    public static void LoadPlaylists()
    {
        Log.Information("PlaylistsViewModel - LoadPlaylists");

        List<Playlist> playlists = MusicLibrary.GetPlaylists();

        foreach (Playlist p in playlists)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;

            PlaylistTab tab = AddPlaylistTab(p);

            Log.Information($"LoadPlaylists - added PlaylistTab {tab}");
        }
    }

    public static PlaylistTab AddPlaylistTab(Playlist p)
    {
        PlaylistTab tab = new PlaylistTab
        {
            Header = p.Name,
            Tracks = LoadPlaylistTracks(p.Name)
        };

        TabList.Add(tab);
        return tab;
    }

    public static void AddSongToPlaylistTab(MediaFile song, string playlistName)
    {
        Log.Information("MainWindow - LoadPlaylistTracks");

        foreach (PlaylistTab tab in TabList)
        {
            if (tab.Header == playlistName)
            {
                tab.Tracks!.Add(song);
            }
        }
    }

    //private Playlist? _selectedPlaylist;
    //private static MediaFile? _selectedTrack;
    //private static PlaylistTab? _selectedTab;

    public void UpdatePlaylistTab(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        if (sender is TabControl tabControl)
        {
            SelectedTab = TabList[tabControl.SelectedIndex];
        }
    }

    private static ObservableCollection<MediaFile> LoadPlaylistTracks(string? playListName)
    {
        Log.Information("MainWindow - LoadPlaylistTracks");

        ObservableCollection<MediaFile> tracks = new();
        List<MediaFile> songs = MusicLibrary.GetSongsFromPlaylist(playListName);

        foreach (MediaFile song in songs)
        {
            tracks.Add(song);
        }

        return tracks;
    }

    private void ListViewItem_Drop(object sender, DragEventArgs e)
    {
        Log.Information("PlaylistList - ListViewItem_Drop");

        string target = (((ListViewItem)(sender)).DataContext as Playlist)!.Name!;

        //Windows.MainWindow win = (Windows.MainWindow)Window.GetWindow(this)!;

        if (e.Data.GetData(typeof(MediaFile)) is MediaFile droppedData && target != null!)
        {
            //if (target != win.SelectedPlaylist?.Name)
            //{
            //    MusicLibrary.AddSongToPlaylist(droppedData.Id, target);
            //    MusicLibrary.RemoveSongFromPlaylist(droppedData.Id, win.SelectedPlaylist!.Name);

            //    // TODO
            //    //int removedIdx = win.TracksTable.TracksTable.Items.IndexOf(droppedData);
            //    //win.TracksTable.TracksTable.Items.RemoveAt(removedIdx);

            //    if (win.SelectedTrack != null)
            //    {
            //        if (droppedData.Id == win.SelectedTrack.Id)
            //        {
            //            win.BackgroundPlaylistName = target;

                        //foreach (Button btn in Helper.FindVisualChildren<Button>(List))
                        //{
                        //    // outline background playlist
                        //    btn.FontWeight = ((btn.Content as ContentPresenter)?.Content as Playlist)?.Name == target ? FontWeights.ExtraBold : FontWeights.Normal;
                        //}
            //        }
            //    }
            //}
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop)!;

            List<string> mp3Files = Helper.GetAllMp3Files(files);

            foreach (string mp3File in mp3Files)
            {
                MediaFile songToAdd = new(mp3File);

                if (MusicLibrary.AddSong(songToAdd))
                {
                    MusicLibrary.AddSongToPlaylist(songToAdd.Id, target);

                    //if (win.SelectedPlaylist == null)
                    //{
                    //    win.SelectPlaylistByName(target!);
                    //}
                    //else if (win.SelectedPlaylist.Name == target)
                    //{
                    //    // TODO
                    //    //win.TracksTable.TracksTable.Items.Add(songToAdd);
                    //}
                    //else
                    //{
                    //    win.SelectPlaylistByName(target!);
                    //}
                }
            }
        }

        Button button = Helper.FindVisualChildren<Button>(sender as ListViewItem).First();

        button.BorderBrush = new SolidColorBrush(Colors.Transparent);
        button.BorderThickness = new Thickness(0);
    }

    private void ListViewItem_PreviewDragEnter(object sender, DragEventArgs e)
    {
        Log.Information("PlaylistList - ListViewItem_PreviewDragEnter");

        Button button = Helper.FindVisualChildren<Button>(sender as ListViewItem).First();

        button.BorderBrush = new SolidColorBrush(Colors.White) { Opacity = 0.4 };
        button.BorderThickness = new Thickness(2);
    }

    private void ListViewItem_PreviewDragLeave(object sender, DragEventArgs e)
    {
        Log.Information("PlaylistList - ListViewItem_PreviewDragLeave");

        Button button = Helper.FindVisualChildren<Button>(sender as ListViewItem).First();

        button.BorderBrush = new SolidColorBrush(Colors.Transparent);
        button.BorderThickness = new Thickness(0);
    }

    private void TextBox_PreviewDragOver(object sender, DragEventArgs e)
    {
        Log.Information("PlaylistList - TexttBox_PreviewDragOver");

        e.Effects = DragDropEffects.Move;
        e.Handled = true; // allows objects to be dropped on TextBox
    }

    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        Log.Information("PlaylistList - MenuItem_Click");

        if (sender is MenuItem menuItem)
        {
            //Button? button = ((ContextMenu)menuItem.Parent).PlacementTarget as Button;

            //Windows.MainWindow win = (Windows.MainWindow)Window.GetWindow(this)!;

            if (Equals(menuItem.Header, "Remove playlist"))
            {
                //if (win.SelectedTrack != null)
                //{
                //    if (MusicLibrary.GetSongsFromPlaylist((menuItem.DataContext as Playlist)!.Name)
                //            .FindIndex(item => item.Id == win.SelectedTrack.Id) != -1)
                //    {
                //        win.SelectedSongRemoved();
                //    }
                //}

                MusicLibrary.RemovePlaylist((menuItem.DataContext as Playlist)!.Name);
                TabList.Remove(menuItem.DataContext as PlaylistTab);

                // TODO
                // win.PlaylistTabs.CurrentPlaylistName.Text = "Playlist not selected";
                // win.TracksTable.TracksTable.Items.Clear();
                //win.SelectedPlaylist = null;
            }
            else if (Equals(menuItem.Header, "Rename"))
            {
                //ContentPresenter? buttonCp = button?.Content as ContentPresenter;

                //TextBox textBox = Helper.FindVisualChildren<TextBox>(buttonCp).First(); // we only have 1 textBox in button

                //textBox.IsReadOnly = false;
                //textBox.Cursor = Cursors.IBeam;
                //textBox.SelectAll();

                //textBox.Focusable = true;
                //textBox.Focus();

                //textBox.FontWeight = FontWeights.ExtraBold;

                //_oldTextBoxText = textBox.Text;
            }
            else if (Equals(menuItem.Header, "Add song(s)"))
            {
                OpenFileDialog openFileDialog = new()
                {
                    Multiselect = true,
                    Title = "Select mp3 file(s)",
                    Filter = "MP3 files (*.mp3)|*.mp3"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string[] files = openFileDialog.FileNames;

                    List<string> mp3Files = Helper.GetAllMp3Files(files);

                    foreach (string mp3File in mp3Files)
                    {
                        MediaFile songToAdd = new(mp3File);

                        string? playlistName = (menuItem.DataContext as Playlist)!.Name;

                        if (MusicLibrary.AddSong(songToAdd))
                        {
                            MusicLibrary.AddSongToPlaylist(songToAdd.Id, playlistName);

                            //if (win.SelectedPlaylist == null)
                            //{
                            //    win.SelectPlaylistByName(playlistName!);
                            //}
                            //else if (win.SelectedPlaylist.Name == playlistName)
                            //{
                            //    // TODO
                            //    //win.TracksTable.TracksTable.Items.Add(songToAdd);
                            //}
                            //else
                            //{
                            //    win.SelectPlaylistByName(playlistName!);
                            //}
                        }
                    }
                }
            }
        }
    }

    //private void TextBox_KeyDown(object sender, KeyEventArgs e)
    //{
    //    if (e.Key == Key.Enter)
    //    {
    //        SetTextBoxToDefaultAndSaveText(sender);
    //    }
    //}

    //private void TextBox_LostFocus(object sender, RoutedEventArgs e)
    //{
    //    SetTextBoxToDefaultAndSaveText(sender);
    //}

    //private void SetTextBoxToDefaultAndSaveText(object sender)
    //{
    //    if (sender is TextBox textBox)
    //    {
    //        textBox.IsReadOnly = true;
    //        textBox.Cursor = Cursors.Hand;

    //        textBox.SelectionLength = 0;

    //        textBox.Focusable = false;

    //        //textBox.FontWeight = FontWeights.DemiBold;

    //        string textBoxText = textBox.Text.Trim();

    //        Windows.MainWindow win = (Windows.MainWindow)Window.GetWindow(this)!;

    //        if (!MusicLibrary.RenamePlaylist(_oldTextBoxText, textBoxText))
    //        {
    //            textBox.Text = _oldTextBoxText;
    //        }
    //        else
    //        {
    //            textBox.Text = textBoxText;
    //            ((textBox.DataContext as Playlist)!).Name = textBoxText;

    //            if (win.SelectedPlaylist != null)
    //            {
    //                if (win.SelectedPlaylist.Name == _oldTextBoxText)
    //                {
    //                    win.RenameSelectedPlaylist(textBoxText);
    //                }
    //            }

    //            if (win.BackgroundPlaylistName != null)
    //            {
    //                if (win.BackgroundPlaylistName == _oldTextBoxText)
    //                {
    //                    win.BackgroundPlaylistName = textBoxText;
    //                }
    //            }
    //        }

    //        TabList.Items.Refresh(); // list item goes to state before renaming without this line :)

    //        Task unused = OutlineBackgroundPlaylist(textBox.Text);
    //    }
    //}

    //private async Task OutlineBackgroundPlaylist(string textBoxText)
    //{
    //    Log.Information("PlaylistList - OutlineBackgroundPlaylist");

    //    Windows.MainWindow win = (Windows.MainWindow)Window.GetWindow(this)!;

    //    await Task.Delay(10);

    //    if (win.BackgroundPlaylistName != null)
    //    {
    //        // background playlist outlining was lost after refresh 
    //        if (win.BackgroundPlaylistName == textBoxText)
    //        {
    //            foreach (Button btn in Helper.FindVisualChildren<Button>(List))
    //            {
    //                if (((btn.Content as ContentPresenter)!.Content as Playlist)!.Name == textBoxText)
    //                {
    //                    btn.FontWeight = FontWeights.ExtraBold;
    //                }
    //            }
    //        }
    //    }
    //}
}