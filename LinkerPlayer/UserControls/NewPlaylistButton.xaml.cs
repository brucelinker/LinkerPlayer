using LinkerPlayer.Audio;
using System;
using System.Windows;
using System.Windows.Input;
using LinkerPlayer.Core;
using LinkerPlayer.Models;

namespace LinkerPlayer.UserControls;

public partial class NewPlaylistButton
{
    public NewPlaylistButton()
    {
        InitializeComponent();
    }

    private void AddButtonClick(object sender, RoutedEventArgs e)
    {
        EnterNamePopup.IsOpen = true;
        PopupTextBox.Focus();
    }

    private void PopupTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string popupTextBoxText = PopupTextBox.Text.Trim();

            if (!string.IsNullOrEmpty(popupTextBoxText))
            {
                var win = (Windows.MainWindow)Window.GetWindow(this)!;

                if (MusicLibrary.AddPlaylist(new Playlist { Name = popupTextBoxText }))
                {
                    win.PlaylistList.List.Items.Add(new Playlist { Name = popupTextBoxText });

                    win.SelectPlaylistByName(popupTextBoxText);
                }
                else
                {
                    win.InfoSnackbar.MessageQueue?.Clear();
                    win.InfoSnackbar.MessageQueue?.Enqueue($"Playlist named {popupTextBoxText} already exists", null,
                        null, null, false, true, TimeSpan.FromSeconds(2));
                }

                PopupTextBox.Text = "";
                EnterNamePopup.IsOpen = false;
            }
        }
    }
}