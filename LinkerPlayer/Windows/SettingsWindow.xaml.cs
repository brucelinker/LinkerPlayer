using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using MaterialDesignThemes.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace LinkerPlayer.Windows;

public partial class SettingsWindow
{
    private readonly ThemeManager _themeManager = new();

    public SettingsWindow()
    {
        InitializeComponent();

        DataContext = this;

        WinMax.DoSourceInitialized(this);

        PreviewKeyDown += Window_PreviewKeyDown;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        MainWindow? mainWindow = (Owner as MainWindow);

        foreach (string device in OutputDevice.GetOutputDevicesList())
        {
            MainOutputDevicesList.Items.Add(device);
            //AdditionalOutputDevicesList.Items.Add(device);
        }

        if (MainOutputDevicesList.Items.Contains(Properties.Settings.Default.MainOutputDevice))
        {
            MainOutputDevicesList.SelectedItem = Properties.Settings.Default.MainOutputDevice;
        }
        else
        {
            MainOutputDevicesList.SelectedItem = OutputDevice.GetOutputDeviceNameById(mainWindow!.PlayerEngine.GetOutputDeviceId());
        }

        //if (AdditionalOutputDevicesList.Items.Contains(Properties.Settings.Default.AdditionalOutputDevice))
        //{
        //    AdditionalOutputDevicesList.SelectedItem = Properties.Settings.Default.AdditionalOutputDevice;
        //}
        //else
        //{
        //    AdditionalOutputDevicesList.SelectedItem = DeviceControl.GetOutputDeviceNameById(0);
        //}

        //AdditionalOutputEnabled.IsChecked = Properties.Settings.Default.AdditionalOutputEnabled;
        //EqualizerOnStartEnabled.IsChecked = Properties.Settings.Default.EqualizerOnStartEnabled;

        int selectedThemeIndex = ThemeManager.StringToThemeColorIndex(Properties.Settings.Default.SelectedTheme);
        if (ThemesList.Items.Count >= 0 && selectedThemeIndex <= ThemesList.Items.Count)
        {
            ThemesList.SelectedIndex = selectedThemeIndex;
        }
        else
        {
            ThemesList.SelectedIndex = (int)ThemeColors.Dark;
        }

        _themeManager.ModifyTheme((ThemeColors)ThemesList.SelectedIndex);

        //VisualizationEnabled.IsChecked = Properties.Settings.Default.VisualizationEnabled;
        //MinimizeToTrayEnabled.IsChecked = Properties.Settings.Default.MinimizeToTrayEnabled;

        //PopulateHotkeyStackPanel();
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        //MainWindow? mainWindow = (Owner as MainWindow);

        ComboBoxItem selectedItem = ((sender as ComboBox)!.SelectedItem as ComboBoxItem)!;

        ThemeColors selectedTheme = (ThemeColors)selectedItem.Tag;

        _themeManager.ModifyTheme(selectedTheme);
    }

    //private static string GetCurrentTheme()
    //{
    //    PaletteHelper paletteHelper = new PaletteHelper();
    //    ITheme theme = paletteHelper.GetTheme();

    //    return theme.ToString() ?? Theme.Dark.ToString()!;
    //}

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        MainWindow? mainWindow = (Owner as MainWindow);

        if (MainOutputDevicesList.SelectedItem.ToString() != Properties.Settings.Default.MainOutputDevice || 
            MainOutputDevicesList.SelectedItem.ToString() != OutputDevice.GetOutputDeviceNameById(mainWindow!.PlayerEngine.GetOutputDeviceId()))
        {
            Properties.Settings.Default.MainOutputDevice = MainOutputDevicesList.SelectedItem.ToString();
            mainWindow!.PlayerEngine.ReselectOutputDevice(Properties.Settings.Default.MainOutputDevice!);
        }

        //if (AdditionalOutputDevicesList.SelectedItem != null)
        //{
        //    changedAdditionalDevice = (AdditionalOutputDevicesList.SelectedItem.ToString() != Properties.Settings.Default.AdditionalOutputDevice)
        //        || (AdditionalOutputDevicesList.SelectedItem.ToString() != DeviceControl.GetOutputDeviceNameById(0));
        //    Properties.Settings.Default.AdditionalOutputDevice = AdditionalOutputDevicesList.SelectedItem.ToString();
        //}

        //bool changedAdditionalEnabled = AdditionalOutputEnabled.IsChecked.GetValueOrDefault() != Properties.Settings.Default.AdditionalOutputEnabled;

        //if ((changedAdditionalDevice && AdditionalOutputEnabled.IsChecked.GetValueOrDefault()) ||
        //    (changedAdditionalEnabled && AdditionalOutputEnabled.IsChecked.GetValueOrDefault()))
        //{

        //    if (AdditionalOutputDevicesList.SelectedItem != null)
        //    {
        //        Properties.Settings.Default.AdditionalOutputEnabled = true;

        //        mainWindow.AudioStreamControl.ActivateAdditionalMusic(Properties.Settings.Default.AdditionalOutputDevice!);
        //        //mainWindow.AudioStreamControl.AdditionalMusic!.MusicVolume = (float)mainWindow.PlayerControls.AdditionalVolumeSlider.Value / 100;
        //        mainWindow.AudioStreamControl.AdditionalMusic.StoppedEvent += mainWindow.Music_StoppedEvent!;
        //    }
        //    else
        //    {
        //        Properties.Settings.Default.AdditionalOutputEnabled = false;
        //        AdditionalOutputEnabled.IsChecked = false;
        //    }
        //}
        //else if (changedAdditionalEnabled && !AdditionalOutputEnabled.IsChecked.GetValueOrDefault())
        //{
        //    Properties.Settings.Default.AdditionalOutputEnabled = false;

        //    if (mainWindow.AudioStreamControl.AdditionalMusic != null)
        //    {
        //        mainWindow.AudioStreamControl.AdditionalMusic.CloseStream();
        //        mainWindow.AudioStreamControl.AdditionalMusic = null;
        //    }
        //}

        //mainWindow.PlayerControls.AdditionalVolumeSlider.IsEnabled = Properties.Settings.Default.AdditionalOutputEnabled;
        //mainWindow.PlayerControls.AdditionalVolumeButton.IsEnabled = Properties.Settings.Default.AdditionalOutputEnabled;

        if (ThemesList.SelectedIndex != ThemeManager.StringToThemeColorIndex(Properties.Settings.Default.SelectedTheme))
        {
            Properties.Settings.Default.SelectedTheme = ThemeManager.IndexToThemeColorString(ThemesList.SelectedIndex);
        }

        //Properties.Settings.Default.MinimizeToTrayEnabled = MinimizeToTrayEnabled.IsChecked.GetValueOrDefault();
        //Properties.Settings.Default.EqualizerOnStartEnabled = EqualizerOnStartEnabled.IsChecked.GetValueOrDefault();

        //if (VisualizationEnabled.IsChecked.GetValueOrDefault() != Properties.Settings.Default.VisualizationEnabled)
        //{
        //    Properties.Settings.Default.VisualizationEnabled = VisualizationEnabled.IsChecked.GetValueOrDefault();
        //    mainWindow.VisualizationEnabled = Properties.Settings.Default.VisualizationEnabled;

        //    if (Properties.Settings.Default.VisualizationEnabled)
        //    {
        //        mainWindow.StartVisualization();
        //    }
        //    else
        //    {
        //        mainWindow.StopVisualization();
        //    }
        //}

        foreach (KeyValuePair<string, string> prop in _tempHotkeys)
        {
            if (prop.Key.EndsWith("Hotkey"))
            {
                Properties.Settings.Default[prop.Key] = _tempHotkeys[prop.Key];
            }
        }

        Properties.Settings.Default.Save();

        InfoSnackbar.MessageQueue?.Clear();
        InfoSnackbar.MessageQueue?.Enqueue("Saved!", null, null, null, false, true, TimeSpan.FromSeconds(1));
    }

    string _editedHotkey = "";
    private readonly Dictionary<string, string> _tempHotkeys = new();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (string.IsNullOrEmpty(_editedHotkey))
        {
            return;
        }

        if (!IsModifierKey(e.Key))
        {
            string newHotkey;

            if (e.KeyboardDevice.Modifiers != ModifierKeys.None)
            {
                newHotkey = e.KeyboardDevice.Modifiers + " + " + e.Key;
            }
            else
            {
                newHotkey = e.Key.ToString();
            }

            if (_tempHotkeys[_editedHotkey] == newHotkey)
            {
                _editedHotkey = "";
                e.Handled = true;
                return;
            }

            bool hotkeyIsUsed = false;

            foreach (KeyValuePair<string, string> prop in _tempHotkeys)
            {
                if (prop.Key.EndsWith("Hotkey"))
                {
                    if (prop.Value == newHotkey)
                    {
                        hotkeyIsUsed = true;

                        InfoSnackbar.MessageQueue?.Clear();
                        InfoSnackbar.MessageQueue?.Enqueue($"This one is already used by {prop.Key}, try another one", null, null, null, false, true, TimeSpan.FromSeconds(1));

                        break;
                    }
                }
            }

            if (!hotkeyIsUsed)
            {
                ((FindName(_editedHotkey) as TextBlock)!).Text = newHotkey;
                _tempHotkeys[_editedHotkey] = newHotkey;
                _editedHotkey = "";
            }
        }

        e.Handled = true;
    }

    private void EditHotkey(object sender, RoutedEventArgs e)
    {
        string name = (sender as Button)!.Name;
        _editedHotkey = name.Substring(0, name.Length - 3); // e.g. PlayPauseHotkeyBtn -> PlayPauseHotkey

        InfoSnackbar.MessageQueue?.Clear();
        InfoSnackbar.MessageQueue?.Enqueue("Press new key combination", null, null, null, false, true, TimeSpan.FromSeconds(1));
    }

    private bool IsModifierKey(Key key)
    {
        List<Key> modifierKeys = new List<Key> {
                Key.LeftCtrl, Key.RightCtrl,
                Key.LeftAlt, Key.RightAlt,
                Key.LeftShift, Key.RightShift,
                Key.LWin, Key.RWin,
                Key.System
            };

        return modifierKeys.Contains(key);
    }

//    void PopulateHotkeyStackPanel()
//    {
//        string[] hotkeys = { // names of hotkeys
//                "Play/Pause",
//                "Next Song",
//                "Previous Song",
//                "Increase Main Volume",
//                "Decrease Main Volume"
//            };

//        foreach (string hk in hotkeys)
//        {
//            string name = Regex.Replace(hk, @"[^a-zA-Z0-9_]", ""); // e.g. Play/Pause -> PlayPause

//            Grid grid = new Grid();
//            grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(200) });
//            grid.ColumnDefinitions.Add(new ColumnDefinition());
//            grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(40) });

//  //          TextBlock tb1 = new TextBlock() { Text = hk, Style = (Style)TabControl.FindResource(typeof(TextBlock)) };
//            Grid.SetColumn(tb1, 0);
//            grid.Children.Add(tb1);

//            TextBlock tb2 = new TextBlock
//            {
////                Name = name + "Hotkey", Style = (Style)TabControl.FindResource(typeof(TextBlock)),
//                TextAlignment = TextAlignment.Center,
//                Text = Properties.Settings.Default[name + "Hotkey"].ToString()
//            };

//            _tempHotkeys[name + "Hotkey"] = tb2.Text!;
//            Grid.SetColumn(tb2, 1);
//            grid.Children.Add(tb2);
//            HotkeyStackPanel.RegisterName(tb2.Name, tb2);

//            Button btn = new Button() { Name = name + "HotkeyBtn", Style = (Style)FindResource("NoStylingButton") };
//            btn.Click += EditHotkey;

//            PackIcon packIcon = new PackIcon() { Kind = PackIconKind.PencilOutline, Foreground = Brushes.White };
//            btn.Content = packIcon;

//            Grid.SetColumn(btn, 2);
//            grid.Children.Add(btn);
//            HotkeyStackPanel.RegisterName(btn.Name, btn);

//            HotkeyStackPanel.Children.Add(grid);
//        }
//    }

    private void Window_Closing(object sender, EventArgs e)
    {

    }

    //private void Window_StateChanged(object sender, EventArgs e)
    //{
    //    if (WindowState == WindowState.Maximized)
    //    {
    //        Uri uri = new Uri("/Images/Icons/restore.png", UriKind.Relative);
    //        ImageSource imgSource = new BitmapImage(uri);
    //        TitlebarButtons.MaximizeButtonImage.Source = imgSource;
    //    }
    //    else if (WindowState == WindowState.Normal)
    //    {
    //        Uri uri = new Uri("/Images/Icons/maximize.png", UriKind.Relative);
    //        ImageSource imgSource = new BitmapImage(uri);
    //        TitlebarButtons.MaximizeButtonImage.Source = imgSource;
    //    }
    //}

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Helper.FindVisualChildren<Grid>(this).FirstOrDefault()!.Focus();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Window? win = Window.GetWindow(this);
        if (win != null) win.Close();
    }

    private void ButtonMouseEnter(object sender, MouseEventArgs e)
    {
        ((sender as Button)?.Content as Image)!.Opacity = 1;
    }

    private void ButtonMouseLeave(object sender, MouseEventArgs e)
    {
        (((sender as Button)?.Content as Image)!).Opacity = 0.6;
    }
}
