using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using Microsoft.Extensions.DependencyInjection;
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
    private static readonly SettingsManager SettingsManager = App.AppHost.Services.GetRequiredService<SettingsManager>();

    private const string DefaultDevice = "Default";


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

        if (MainOutputDevicesList.Items.Contains(SettingsManager.Settings.MainOutputDevice))
        {
            MainOutputDevicesList.SelectedItem = SettingsManager.Settings.MainOutputDevice;
        }
        else
        {
            MainOutputDevicesList.SelectedItem = OutputDeviceManager.GetCurrentDeviceName();
        }

        int selectedThemeIndex = _themeManager.StringToThemeColorIndex(SettingsManager.Settings.SelectedTheme);
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
        string deviceName = MainOutputDevicesList.SelectedItem.ToString() ?? DefaultDevice;

        if (deviceName != SettingsManager.Settings.MainOutputDevice || 
            deviceName != OutputDeviceManager.GetCurrentDeviceName())
        {
            SettingsManager.Settings.MainOutputDevice = deviceName!;
            SettingsManager.SaveSettings(nameof(AppSettings.MainOutputDevice));
            _audioEngine.ReselectOutputDevice(SettingsManager.Settings.MainOutputDevice!);
        }
        
        if (ThemesList.SelectedIndex != _themeManager.StringToThemeColorIndex(SettingsManager.Settings.SelectedTheme))
        {
            SettingsManager.Settings.SelectedTheme = _themeManager.IndexToThemeColorString(ThemesList.SelectedIndex);
            SettingsManager.SaveSettings(nameof(AppSettings.SelectedTheme));
        }

        //foreach (KeyValuePair<string, string> prop in _tempHotkeys)
        //{
        //    if (prop.Key.EndsWith("Hotkey"))
        //    {
                //Properties.Settings.Default[prop.Key] = _tempHotkeys[prop.Key];
        //    }
        //}

        //Properties.Settings.Default.Save();

        Window? win = GetWindow(this);
        win?.Close();
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
}
