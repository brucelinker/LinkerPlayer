using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace LinkerPlayer.UserControls;

public partial class FunctionButtons
{
    public FunctionButtons()
    {
        InitializeComponent();
    }

    private bool _isSettingsWindowOpen;
    private SettingsWindow? _settingsWin;

    private bool _isEqualizerWindowOpen;
    private CustomEqualizer? _equalizerWin;

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isSettingsWindowOpen)
        {
            if (_settingsWin is { WindowState: WindowState.Minimized })
            {
                _settingsWin.WindowState = WindowState.Normal;
            }

            return;
        }

        _settingsWin = new SettingsWindow
        {
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        _settingsWin.Closed += (_, _) => { _isSettingsWindowOpen = false; };
        _settingsWin.Closing += (_, _) => { _settingsWin.Owner = null; };
        _isSettingsWindowOpen = true;

        _settingsWin.Show();
    }

    private async void ConvertButton_Click(object sender, RoutedEventArgs e)
    {
        ConvertButton.IsHitTestVisible = false;
        DialogResult fileDialogResult = DialogResult.None;
        FolderBrowserDialog folderDialog = new FolderBrowserDialog()
        {
            RootFolder = Environment.SpecialFolder.MyMusic
        };

        await Task.Run(() =>
        {
            // because ShowDialog blocks animations
            fileDialogResult = folderDialog.ShowDialog();
        });

        ConvertButton.IsHitTestVisible = true;

        if (fileDialogResult == DialogResult.OK)
        {
            string selectedFolderPath = folderDialog.SelectedPath;
            DirectoryInfo dirInfo = new DirectoryInfo(selectedFolderPath);
            List<FileInfo> files = dirInfo.GetFiles("*.mp3", SearchOption.AllDirectories).ToList();

            if (!files.Any())
            {
                MainWindow win = (MainWindow)Window.GetWindow(this)!;
                win.InfoSnackbar.MessageQueue?.Clear();
                win.InfoSnackbar.MessageQueue?.Enqueue($"No files were found in {selectedFolderPath}.", null, null, null,
                    false, true, TimeSpan.FromSeconds(3));
            }

            foreach (FileInfo? file in files)
            {
                Song song = new Song { Path = file.FullName };

                if (MusicLibrary.AddSong(song))
                {
                    MainWindow win = (MainWindow)Window.GetWindow(this)!;

                    Playlist? selectedPlaylist = win.SelectedPlaylist;

                    if (selectedPlaylist == null)
                    {
                        selectedPlaylist = MusicLibrary.GetPlaylists().FirstOrDefault();

                        if (selectedPlaylist != null)
                        {
                            win.SelectPlaylistByName(selectedPlaylist.Name!);

                            MusicLibrary.AddSongToPlaylist(song.Id, selectedPlaylist.Name);
                            win.SongList.List.Items.Add(song);
                        }
                    }
                    else
                    {
                        MusicLibrary.AddSongToPlaylist(song.Id, selectedPlaylist.Name);
                        win.SongList.List.Items.Add(song);
                    }
                }
            }
        }
    }

    private void EqualizerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isEqualizerWindowOpen)
        {
            if (_equalizerWin is { WindowState: WindowState.Minimized })
            {
                _equalizerWin.WindowState = WindowState.Normal;
            }

            return;
        }

        _equalizerWin = new CustomEqualizer
        {
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        _equalizerWin.Closed += (_, _) => { _isEqualizerWindowOpen = false; };
        _equalizerWin.Closing += (_, _) => { _equalizerWin.Owner = null; };
        _isEqualizerWindowOpen = true;

        _equalizerWin.Show();

        MainWindow win = (MainWindow)Window.GetWindow(this)!;

        _equalizerWin.StartStopText.Text = win.AudioStreamControl.MainMusic!.IsEqualizerWorking ? "Stop" : "Start";

        _equalizerWin.LoadSelectedBand(win.SelectedBandsSettings);

        if (_equalizerWin.StartStopText.Text == "Start")
        {
            _equalizerWin.ButtonsSetEnabledState(false);
            _equalizerWin.SliderSetEnabledState(false);
        }
    }
}