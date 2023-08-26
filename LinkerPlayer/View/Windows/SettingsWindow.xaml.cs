using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Utils;
using MaterialDesignThemes.Wpf;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WinForms = System.Windows.Forms;

namespace LinkerPlayer.View.Windows;

public partial class SettingsWindow : Window {
    public SettingsWindow() {
        InitializeComponent();
        DataContext = this;
        WinMax.DoSourceInitialized(this);

        PreviewKeyDown += Window_PreviewKeyDown;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) {
        var win = (Owner as MainWindow);

        foreach (var device in DeviceControll.GetOutputDevicesList()) {
            MainOutputDevicesList.Items.Add(device);
            AdditionalOutputDevicesList.Items.Add(device);
            MicOutputDevicesList.Items.Add(device);
        }

        foreach (var device in DeviceControll.GetInputDevicesList()) {
            InputDevicesList.Items.Add(device);
        }

        if (MainOutputDevicesList.Items.Contains(LinkerPlayer.Properties.Settings.Default.MainOutputDevice)) {
            MainOutputDevicesList.SelectedItem = LinkerPlayer.Properties.Settings.Default.MainOutputDevice;
        }
        else {
            MainOutputDevicesList.SelectedItem = DeviceControll.GetOutputDeviceNameById(win.AudioStreamControl.MainMusic.GetOutputDeviceId());
        }

        if (AdditionalOutputDevicesList.Items.Contains(LinkerPlayer.Properties.Settings.Default.AdditionalOutputDevice)) {
            AdditionalOutputDevicesList.SelectedItem = LinkerPlayer.Properties.Settings.Default.AdditionalOutputDevice;
        }
        else {
            AdditionalOutputDevicesList.SelectedItem = DeviceControll.GetOutputDeviceNameById(0);
        }

        if (MicOutputDevicesList.Items.Contains(LinkerPlayer.Properties.Settings.Default.MicOutputDevice)) {
            MicOutputDevicesList.SelectedItem = LinkerPlayer.Properties.Settings.Default.MicOutputDevice;
        }
        else {
            MicOutputDevicesList.SelectedItem = DeviceControll.GetOutputDeviceNameById(0);
        }

        InputDevicesList.SelectedItem = LinkerPlayer.Properties.Settings.Default.InputDevice;

        MicOutputEnabled.IsChecked = LinkerPlayer.Properties.Settings.Default.MicOutputEnabled;
        AdditionalOutputEnabled.IsChecked = LinkerPlayer.Properties.Settings.Default.AdditionalOutputEnabled;
        EqualizerOnStartEnabled.IsChecked = LinkerPlayer.Properties.Settings.Default.EqualizerOnStartEnabled;

        if (string.IsNullOrEmpty(LinkerPlayer.Properties.Settings.Default.DownloadsFolder)) {
            string downloadsFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            DownloadsFolder.Text = downloadsFolderPath;
        }
        else {
            DownloadsFolder.Text = LinkerPlayer.Properties.Settings.Default.DownloadsFolder;
        }
            
        VisualizationEnabled.IsChecked = LinkerPlayer.Properties.Settings.Default.VisualizationEnabled;
        MinimizeToTrayEnabled.IsChecked = LinkerPlayer.Properties.Settings.Default.MinimizeToTrayEnabled;

        PopulateHotkeyStackPanel();
    }

    private void Save_Click(object sender, RoutedEventArgs e) {
        var win = (Owner as MainWindow); 

        if (MainOutputDevicesList.SelectedItem.ToString() != LinkerPlayer.Properties.Settings.Default.MainOutputDevice || MainOutputDevicesList.SelectedItem.ToString() != DeviceControll.GetOutputDeviceNameById(win.AudioStreamControl.MainMusic.GetOutputDeviceId())) {
            LinkerPlayer.Properties.Settings.Default.MainOutputDevice = MainOutputDevicesList.SelectedItem.ToString();
            win.AudioStreamControl.MainMusic.ReselectOutputDevice(LinkerPlayer.Properties.Settings.Default.MainOutputDevice);
        }

        if (MicOutputDevicesList.SelectedItem != null) {
            LinkerPlayer.Properties.Settings.Default.MicOutputDevice = MicOutputDevicesList.SelectedItem.ToString();
        }

        if (InputDevicesList.SelectedItem != null) {
            LinkerPlayer.Properties.Settings.Default.InputDevice = InputDevicesList.SelectedItem.ToString();
        }

        LinkerPlayer.Properties.Settings.Default.MicOutputEnabled = MicOutputEnabled.IsChecked.GetValueOrDefault();

        if (LinkerPlayer.Properties.Settings.Default.MicOutputEnabled &&
            !string.IsNullOrEmpty(LinkerPlayer.Properties.Settings.Default.MicOutputDevice) &&
            !string.IsNullOrEmpty(LinkerPlayer.Properties.Settings.Default.InputDevice)) {

            if (win.AudioStreamControl.Microphone != null) {
                win.AudioStreamControl.Microphone.CloseStream();
                win.AudioStreamControl.Microphone = null;
            }

            win.AudioStreamControl.ActivateMic(LinkerPlayer.Properties.Settings.Default.InputDevice, LinkerPlayer.Properties.Settings.Default.MicOutputDevice);
            win.AudioStreamControl.Microphone.InputDeviceVolume = (float)LinkerPlayer.Properties.Settings.Default.MicVolumeSliderValue / 100;
        }
        else {
            if (win.AudioStreamControl.Microphone != null) {
                win.AudioStreamControl.Microphone.CloseStream();
                win.AudioStreamControl.Microphone = null;
            }

            LinkerPlayer.Properties.Settings.Default.MicOutputEnabled = false;
            MicOutputEnabled.IsChecked = false;
        }

        win.BottomControlPanel.MicVolumeSlider.IsEnabled = LinkerPlayer.Properties.Settings.Default.MicOutputEnabled;
        win.BottomControlPanel.MicVolumeButton.IsEnabled = LinkerPlayer.Properties.Settings.Default.MicOutputEnabled;

        bool changedAdditionalDevice = false;
        bool changedAdditionalEnabled = false;

        if (AdditionalOutputDevicesList.SelectedItem != null) {
            changedAdditionalDevice = (AdditionalOutputDevicesList.SelectedItem.ToString() != LinkerPlayer.Properties.Settings.Default.AdditionalOutputDevice) 
                                      || (AdditionalOutputDevicesList.SelectedItem.ToString() != DeviceControll.GetOutputDeviceNameById(0));
            LinkerPlayer.Properties.Settings.Default.AdditionalOutputDevice = AdditionalOutputDevicesList.SelectedItem.ToString();
        }

        changedAdditionalEnabled = AdditionalOutputEnabled.IsChecked.GetValueOrDefault() != LinkerPlayer.Properties.Settings.Default.AdditionalOutputEnabled;

        if ((changedAdditionalDevice && AdditionalOutputEnabled.IsChecked.GetValueOrDefault()) || 
            (changedAdditionalEnabled && AdditionalOutputEnabled.IsChecked.GetValueOrDefault())) {

            if (AdditionalOutputDevicesList.SelectedItem != null) {
                LinkerPlayer.Properties.Settings.Default.AdditionalOutputEnabled = true;

                win.AudioStreamControl.ActivateAdditionalMusic(LinkerPlayer.Properties.Settings.Default.AdditionalOutputDevice);
                win.AudioStreamControl.AdditionalMusic.MusicVolume = (float)win.BottomControlPanel.AdditionalVolumeSlider.Value / 100;
                win.AudioStreamControl.AdditionalMusic.StoppedEvent += win.Music_StoppedEvent;
            }
            else {
                LinkerPlayer.Properties.Settings.Default.AdditionalOutputEnabled = false;
                AdditionalOutputEnabled.IsChecked = false;
            }
        }
        else if (changedAdditionalEnabled && !AdditionalOutputEnabled.IsChecked.GetValueOrDefault()) {
            LinkerPlayer.Properties.Settings.Default.AdditionalOutputEnabled = false;

            if (win.AudioStreamControl.AdditionalMusic != null) {
                win.AudioStreamControl.AdditionalMusic.CloseStream();
                win.AudioStreamControl.AdditionalMusic = null;
            }
        }

        win.BottomControlPanel.AdditionalVolumeSlider.IsEnabled = LinkerPlayer.Properties.Settings.Default.AdditionalOutputEnabled;
        win.BottomControlPanel.AdditionalVolumeButton.IsEnabled = LinkerPlayer.Properties.Settings.Default.AdditionalOutputEnabled;

        LinkerPlayer.Properties.Settings.Default.DownloadsFolder = DownloadsFolder.Text;
        LinkerPlayer.Properties.Settings.Default.MinimizeToTrayEnabled = MinimizeToTrayEnabled.IsChecked.GetValueOrDefault();
        LinkerPlayer.Properties.Settings.Default.EqualizerOnStartEnabled = EqualizerOnStartEnabled.IsChecked.GetValueOrDefault();

        if (VisualizationEnabled.IsChecked.GetValueOrDefault() != LinkerPlayer.Properties.Settings.Default.VisualizationEnabled) {
            LinkerPlayer.Properties.Settings.Default.VisualizationEnabled = VisualizationEnabled.IsChecked.GetValueOrDefault();
            win.VisualizationEnabled = LinkerPlayer.Properties.Settings.Default.VisualizationEnabled;

            if (LinkerPlayer.Properties.Settings.Default.VisualizationEnabled) {
                win.StartVisualization();
            }
            else {
                win.StopVisualization();
            }
        }

        foreach (KeyValuePair<string, string> prop in tempHotkeys) {
            if (prop.Key.EndsWith("Hotkey")) {
                LinkerPlayer.Properties.Settings.Default[prop.Key] = tempHotkeys[prop.Key];
            }
        }

        LinkerPlayer.Properties.Settings.Default.Save();

        InfoSnackbar.MessageQueue?.Clear();
        InfoSnackbar.MessageQueue?.Enqueue("Saved!", null, null, null, false, true, TimeSpan.FromSeconds(1));
    }

    private void EditDownloadsFolder(object sender, RoutedEventArgs e) {
        var dialog = new WinForms.FolderBrowserDialog();
        dialog.InitialDirectory = DownloadsFolder.Text;

        var res = dialog.ShowDialog();

        if (res == WinForms.DialogResult.OK) {
            DownloadsFolder.Text = dialog.SelectedPath;
        }
    }

    string editedHotkey = "";
    Dictionary<string, string> tempHotkeys = new Dictionary<string, string>();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) { 
        if (string.IsNullOrEmpty(editedHotkey)) {
            return;
        }

        if (!IsModifierKey(e.Key)) {
            var newHotkey = "";

            if (e.KeyboardDevice.Modifiers != ModifierKeys.None) {
                newHotkey = e.KeyboardDevice.Modifiers + " + " + e.Key;
            }
            else {
                newHotkey = e.Key.ToString();
            }

            if (tempHotkeys[editedHotkey] == newHotkey) {
                editedHotkey = "";
                e.Handled = true;
                return;
            }

            bool hotkeyIsUsed = false;

            foreach (KeyValuePair<string, string> prop in tempHotkeys) {
                if (prop.Key.EndsWith("Hotkey")) {
                    if (prop.Value == newHotkey) {
                        hotkeyIsUsed = true;

                        InfoSnackbar.MessageQueue?.Clear();
                        InfoSnackbar.MessageQueue?.Enqueue($"This one is already used by {prop.Key}, try another one", null, null, null, false, true, TimeSpan.FromSeconds(1));

                        break;
                    }
                }
            }

            if (!hotkeyIsUsed) {
                (FindName(editedHotkey) as TextBlock).Text = newHotkey;
                tempHotkeys[editedHotkey] = newHotkey;
                editedHotkey = "";
            }
        }

        e.Handled = true;
    }

    private void EditHotkey(object sender, RoutedEventArgs e) {
        string name = (sender as Button).Name;
        editedHotkey = name.Substring(0, name.Length - 3); // e.g. PlayPauseHotkeyBtn -> PlayPauseHotkey

        InfoSnackbar.MessageQueue?.Clear();
        InfoSnackbar.MessageQueue?.Enqueue("Press new key combination", null, null, null, false, true, TimeSpan.FromSeconds(1));
    }

    private bool IsModifierKey(Key key) {
        List<Key> modifierKeys = new List<Key> {
            Key.LeftCtrl, Key.RightCtrl,
            Key.LeftAlt, Key.RightAlt,
            Key.LeftShift, Key.RightShift,
            Key.LWin, Key.RWin,
            Key.System
        };

        return modifierKeys.Contains(key);
    }

    void PopulateHotkeyStackPanel() {
        string[] hotkeys = { // names of hotkeys
            "Play/Pause",
            "Next Song",
            "Previous Song",
            "Increase Main Volume",
            "Decrease Main Volume"
        };

        foreach (var hk in hotkeys) {
            string name = Regex.Replace(hk, @"[^a-zA-Z0-9_]", ""); // e.g. Play/Pause -> PlayPause

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(200) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(40) });

            TextBlock tb1 = new TextBlock() { Text = hk, Style = (Style)TabControl.FindResource(typeof(TextBlock)) };
            Grid.SetColumn(tb1, 0);
            grid.Children.Add(tb1);

            TextBlock tb2 = new TextBlock() { Name = name + "Hotkey", Style = (Style)TabControl.FindResource(typeof(TextBlock)), TextAlignment = TextAlignment.Center };
            tb2.Text = LinkerPlayer.Properties.Settings.Default[name + "Hotkey"].ToString();
            tempHotkeys[name + "Hotkey"] = tb2.Text;
            Grid.SetColumn(tb2, 1);
            grid.Children.Add(tb2);
            HotkeyStackPanel.RegisterName(tb2.Name, tb2);

            Button btn = new Button() { Name = name + "HotkeyBtn", Style = (Style)FindResource("NoStylingButton") };
            btn.Click += EditHotkey;

            PackIcon packIcon = new PackIcon() { Kind = PackIconKind.PencilOutline, Foreground = Brushes.White};
            btn.Content = packIcon;

            Grid.SetColumn(btn, 2);
            grid.Children.Add(btn);
            HotkeyStackPanel.RegisterName(btn.Name, btn);

            HotkeyStackPanel.Children.Add(grid);
        }
    }

    private void Window_Closing(object sender, EventArgs e) {

    }

    private void Window_StateChanged(object sender, EventArgs e) {
        if (WindowState == WindowState.Maximized) {
            Uri uri = new Uri("/Resources/Images/restore.png", UriKind.Relative);
            ImageSource imgSource = new BitmapImage(uri);
            TitlebarButtons.MaximizeButtonImage.Source = imgSource;
        }
        else if (WindowState == WindowState.Normal) {
            Uri uri = new Uri("/Resources/Images/maximize.png", UriKind.Relative);
            ImageSource imgSource = new BitmapImage(uri);
            TitlebarButtons.MaximizeButtonImage.Source = imgSource;
        }
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e) {
        Helper.FindVisualChildren<Grid>(this).FirstOrDefault().Focus();
    }
}