using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LinkerPlayer.Windows;

public partial class SettingsWindow
{
    private readonly ThemeManager _themeManager = new();
    private readonly AudioEngine _audioEngine;

    public SettingsWindow()
    {
        InitializeComponent();

        DataContext = this;
        _audioEngine = AudioEngine.Instance;

        WinMax.DoSourceInitialized(this);

        PreviewKeyDown += Window_PreviewKeyDown;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        foreach (string device in OutputDeviceManager.GetOutputDevicesList())
        {
            MainOutputDevicesList.Items.Add(device);
        }

        if (MainOutputDevicesList.Items.Contains(Properties.Settings.Default.MainOutputDevice))
        {
            MainOutputDevicesList.SelectedItem = Properties.Settings.Default.MainOutputDevice;
        }
        else
        {
            MainOutputDevicesList.SelectedItem = OutputDeviceManager.GetCurrentDeviceName();
        }

        int selectedThemeIndex = _themeManager.StringToThemeColorIndex(Properties.Settings.Default.SelectedTheme);
        if (ThemesList.Items.Count >= 0 && selectedThemeIndex <= ThemesList.Items.Count)
        {
            ThemesList.SelectedIndex = selectedThemeIndex;
        }
        else
        {
            ThemesList.SelectedIndex = (int)ThemeColors.Dark;
        }

        _themeManager.ModifyTheme((ThemeColors)ThemesList.SelectedIndex);
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ComboBoxItem selectedItem = ((sender as ComboBox)!.SelectedItem as ComboBoxItem)!;

        ThemeColors selectedTheme = (ThemeColors)selectedItem.Tag;

        _themeManager.ModifyTheme(selectedTheme);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (MainOutputDevicesList.SelectedItem.ToString() != Properties.Settings.Default.MainOutputDevice || 
            MainOutputDevicesList.SelectedItem.ToString() != OutputDeviceManager.GetCurrentDeviceName())
        {
            Properties.Settings.Default.MainOutputDevice = MainOutputDevicesList.SelectedItem.ToString();
            _audioEngine.ReselectOutputDevice(Properties.Settings.Default.MainOutputDevice!);
        }
        
        if (ThemesList.SelectedIndex != _themeManager.StringToThemeColorIndex(Properties.Settings.Default.SelectedTheme))
        {
            Properties.Settings.Default.SelectedTheme = _themeManager.IndexToThemeColorString(ThemesList.SelectedIndex);
        }

        foreach (KeyValuePair<string, string> prop in _tempHotkeys)
        {
            if (prop.Key.EndsWith("Hotkey"))
            {
                Properties.Settings.Default[prop.Key] = _tempHotkeys[prop.Key];
            }
        }

        Properties.Settings.Default.Save();

        Window? win = Window.GetWindow(this);
        if (win != null) win.Close();
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

    private bool IsModifierKey(Key key)
    {
        List<Key> modifierKeys =
        [
            Key.LeftCtrl, Key.RightCtrl,
            Key.LeftAlt, Key.RightAlt,
            Key.LeftShift, Key.RightShift,
            Key.LWin, Key.RWin,
            Key.System
        ];

        return modifierKeys.Contains(key);
    }


    private void Window_Closing(object sender, EventArgs e)
    {
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
