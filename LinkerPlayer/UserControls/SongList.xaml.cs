using LinkerPlayer.Audio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LinkerPlayer.Core;
using LinkerPlayer.Models;

namespace LinkerPlayer.UserControls;

public partial class SongList
{
    public SongList()
    {
        DataContext = this;
        InitializeComponent();
    }

    public RoutedEventHandler? ClickRowElement;

    private bool _isDragging;
    private Point? _startPoint;
    private string _oldTextBoxText = string.Empty;

    private void ListView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ListView? listView = sender as ListView;
        GridView? gridView = listView?.View as GridView;

        if (listView != null)
        {
            double workingWidth =
                listView.ActualWidth - SystemParameters.VerticalScrollBarWidth; // take into account vertical scrollbar
            if (gridView != null)
            {
                workingWidth -= gridView.Columns.Last().Width;

                gridView.Columns[1].Width = workingWidth * 0.4;
                gridView.Columns[2].Width = workingWidth * 0.6;
            }
        }
    }

    private void StartDrag(object sender)
    {
        _isDragging = true;

        if (sender is ListViewItem draggedItem)
        {
            DragDrop.DoDragDrop(draggedItem, draggedItem.DataContext, DragDropEffects.Move);
        }

        _isDragging = false;
    }

    private void ListViewItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
        {
            Point position = e.GetPosition(null);

            if (_startPoint != null)
            {
                if (Math.Abs(position.X - ((Point)_startPoint).X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - ((Point)_startPoint).Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _startPoint = null;
                    StartDrag(sender);
                }
            }
        }
    }

    private void ListViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(null);
    }

    private void ListViewItem_Drop(object sender, DragEventArgs e)
    {
        Song? droppedData = e.Data.GetData(typeof(Song)) as Song;
        Song? target = ((ListViewItem)(sender)).DataContext as Song;

        Windows.MainWindow win = (Windows.MainWindow)Window.GetWindow(this)!;

        if (droppedData != null && target != null)
        {
            int removedIdx = List.Items.IndexOf(droppedData);
            int targetIdx = List.Items.IndexOf(target);

            if (removedIdx != targetIdx)
            {
                List.Items.RemoveAt(removedIdx);
                List.Items.Insert(targetIdx, droppedData);

                MusicLibrary.RemoveSongFromPlaylist(droppedData.Id, win.SelectedPlaylist?.Name);
                MusicLibrary.AddSongToPlaylist(droppedData.Id, win.SelectedPlaylist?.Name, targetIdx);
            }
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            if (target != null)
            {
                int targetIdx = List.Items.IndexOf(target);

                List<string> mp3Files = Helper.GetAllMp3Files(files);
                mp3Files.Reverse();

                foreach (string mp3File in mp3Files)
                {
                    Song songToAdd = new Song() { Path = mp3File };

                    if (MusicLibrary.AddSong(songToAdd))
                    {
                        MusicLibrary.AddSongToPlaylist(songToAdd.Id, win.SelectedPlaylist?.Name, targetIdx);
                        List.Items.Insert(targetIdx, songToAdd);
                    }
                }
            }
        }

        Button button = Helper.FindVisualChildren<Button>(sender as ListViewItem).First();
        button.BorderBrush = new SolidColorBrush(Colors.Transparent);
        button.BorderThickness = new Thickness(0);

        e.Handled = true; // prevents ListView_Drop from being raised

        if (droppedData != null)
        {
            if (droppedData.Id == win.SelectedSong?.Id)
            {
                List.Items.Refresh();

                OutlineSelectedSong();
            }
        }
    }

    private async void OutlineSelectedSong()
    {
        Windows.MainWindow win = (Windows.MainWindow)Window.GetWindow(this)!;

        await Task.Delay(10);

        foreach (Button buttonToChange in Helper.FindVisualChildren<Button>(List))
        {
            if (((buttonToChange.Content as GridViewRowPresenter)?.Content as Song)?.Id == win.SelectedSong?.Id)
            {
                buttonToChange.FontWeight = FontWeights.ExtraBold;
                break;
            }
        }
    }

    private void ListViewItem_PreviewDragEnter(object sender, DragEventArgs e)
    {
        Button button = Helper.FindVisualChildren<Button>(sender as ListViewItem).First();

        Song? target = ((ListViewItem)(sender)).DataContext as Song;

        if (e.Data.GetData(typeof(Song)) is Song droppedData && target != null)
        {
            int removedIdx = List.Items.IndexOf(droppedData);
            int targetIdx = List.Items.IndexOf(target);

            if (removedIdx != targetIdx)
            {
                button.BorderBrush = new SolidColorBrush(Colors.White) { Opacity = 0.4 };

                button.BorderThickness = removedIdx > targetIdx ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 0, 2);
            }
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            button.BorderBrush = new SolidColorBrush(Colors.White) { Opacity = 0.4 };
            button.BorderThickness = new Thickness(0, 2, 0, 0);
        }
    }

    private void ListViewItem_PreviewDragLeave(object sender, DragEventArgs e)
    {
        Button button = Helper.FindVisualChildren<Button>(sender as ListViewItem).First();

        button.BorderBrush = new SolidColorBrush(Colors.Transparent);
        button.BorderThickness = new Thickness(0);
    }

    private void TextBox_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true; // allows objects to be dropped on TextBox
    }

    private void ListView_Drop(object sender, DragEventArgs e)
    {
        Windows.MainWindow win = (Windows.MainWindow)Window.GetWindow(this)!;
        Playlist? selectedPlaylist = win.SelectedPlaylist;

        if (e.Data.GetDataPresent(DataFormats.FileDrop) && selectedPlaylist != null)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop)!;

            List<string> mp3Files = Helper.GetAllMp3Files(files);

            foreach (string mp3File in mp3Files)
            {
                Song songToAdd = new Song() { Path = mp3File };

                if (MusicLibrary.AddSong(songToAdd))
                {
                    MusicLibrary.AddSongToPlaylist(songToAdd.Id, selectedPlaylist.Name);
                    List.Items.Add(songToAdd);
                }
            }
        }
    }

    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            Windows.MainWindow win = (Windows.MainWindow)Window.GetWindow(this)!;

            if (Equals(menuItem.Header, "Remove from playlist"))
            {
                MusicLibrary.RemoveSongFromPlaylist((menuItem.DataContext as Song)!.Id, win.SelectedPlaylist?.Name);
                MusicLibrary.RemoveSong((menuItem.DataContext as Song)!.Id);

                if (win.SelectedSong != null)
                {
                    if ((menuItem.DataContext as Song)!.Id == win.SelectedSong.Id)
                    {
                        win.SelectedSongRemoved();
                    }
                }

                List.Items.Remove(menuItem.DataContext as Song);
            }
            else if (Equals(menuItem.Header, "Rename"))
            {
                Button? button = ((ContextMenu)menuItem.Parent).PlacementTarget as Button;
                GridViewRowPresenter? buttonGridViewRowPresenter = button?.Content as GridViewRowPresenter;

                TextBox textBox =
                    Helper.FindVisualChildren<TextBox>(buttonGridViewRowPresenter).First(); // we only have 1 textBox in button

                textBox.IsReadOnly = false;
                textBox.Cursor = Cursors.IBeam;
                textBox.SelectAll();

                textBox.Focusable = true;
                textBox.Focus();

                //textBox.FontWeight = FontWeights.Bold;

                _oldTextBoxText = textBox.Text;

                _isDragging = true; // prevents dragging while entering and allows text selection with mouse
            }
        }
    }

    private void SetTextBoxToDefaultAndSaveText(object sender)
    {
        if (sender is TextBox textBox)
        {
            textBox.IsReadOnly = true;
            textBox.Cursor = Cursors.Hand;

            textBox.SelectionLength = 0;

            textBox.Focusable = false;

            //textBox.FontWeight = FontWeights.Normal;

            string textBoxText = textBox.Text.Trim();

            if (textBoxText != _oldTextBoxText)
            {
                if (!MusicLibrary.RenameSong((textBox.DataContext as Song)!.Id, textBoxText))
                {
                    textBox.Text = _oldTextBoxText;
                }
                else
                {
                    textBox.Text = textBoxText;
                    ((textBox.DataContext as Song)!).Name = textBoxText;

                    Windows.MainWindow win = (Windows.MainWindow)Window.GetWindow(this)!;

                    if (win.SelectedSong != null)
                    {
                        if (win.SelectedSong.Id == (textBox.DataContext as Song)!.Id)
                        {
                            win.RenameSelectedSong(textBoxText);
                        }
                    }
                }
            }
            else
            {
                textBox.Text = _oldTextBoxText;
            }
        }

        _isDragging = false;
    }

    private void TextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SetTextBoxToDefaultAndSaveText(sender);
        }
    }

    private void TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SetTextBoxToDefaultAndSaveText(sender);
    }

}