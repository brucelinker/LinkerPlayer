using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LinkerPlayer.Windows;

public partial class SettingsWindow
{
    private readonly ThemeManager _themeManager = new();
    private readonly AudioEngine _audioEngine;
    private readonly OutputDeviceManager _outputDeviceManager;
    private readonly ILogger _logger;
    private static readonly SettingsManager SettingsManager = App.AppHost.Services.GetRequiredService<SettingsManager>();

    private const string DefaultDevice = "Default";

    public SettingsWindow(
        AudioEngine audioEngine, 
        OutputDeviceManager outputDeviceManager,
        ILogger<SettingsWindow> logger)
    {
        _audioEngine = audioEngine;
        _outputDeviceManager = outputDeviceManager;
        _logger = logger;

        try
        {
            _logger.Log(LogLevel.Information, "Initializing SettingsWindow");
            InitializeComponent();

            DataContext = this;

            ((App)Application.Current).WindowPlace.Register(this);

            PreviewKeyDown += Window_PreviewKeyDown;
            _logger.Log(LogLevel.Information, "SettingsWindow initialized successfully");
        }
        catch (IOException ex)
        {
            _logger.Log(LogLevel.Error, ex, "IO error in SettingsWindow constructor: {Message}\n{StackTrace}",
                ex.Message, ex.StackTrace);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, ex, "Unexpected error in SettingsWindow constructor: {Message}\n{StackTrace}",
                ex.Message, ex.StackTrace);
            throw;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        foreach (string device in _outputDeviceManager.GetDirectSoundDevices())
        {
            DeviceCombo.Items.Add(device);
        }

        if (DeviceCombo.Items.Contains(SettingsManager.Settings.SelectedOutputDevice))
        {
            DeviceCombo.SelectedItem = SettingsManager.Settings.SelectedOutputDevice;
        }
        else
        {
            DeviceCombo.SelectedItem = _outputDeviceManager.GetCurrentDeviceName();
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
        _logger.Log(LogLevel.Information, "Theme changed to {Theme}", selectedTheme);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        string deviceName = DeviceCombo.SelectedItem.ToString() ?? DefaultDevice;

        if (deviceName != SettingsManager.Settings.SelectedOutputDevice || 
            deviceName != _outputDeviceManager.GetCurrentDeviceName())
        {
            SettingsManager.Settings.SelectedOutputDevice = deviceName!;
            SettingsManager.SaveSettings(nameof(AppSettings.SelectedOutputDevice));
            _audioEngine.ReselectOutputDevice(SettingsManager.Settings.SelectedOutputDevice!);
        }
        
        if (ThemesList.SelectedIndex != _themeManager.StringToThemeColorIndex(SettingsManager.Settings.SelectedTheme))
        {
            SettingsManager.Settings.SelectedTheme = _themeManager.IndexToThemeColorString(ThemesList.SelectedIndex);
            SettingsManager.SaveSettings(nameof(AppSettings.SelectedTheme));
        }

        Window? win = GetWindow(this);
        win?.Hide();
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
        if (win != null) win.Hide();
    }
}
