using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LinkerPlayer.Windows;

public partial class SettingsWindow
{
    private readonly ThemeManager _themeManager = new();
    private readonly AudioEngine _audioEngine;
    private readonly ISettingsManager _settingsManager;
    private readonly ILogger _logger;

    private const string DefaultDeviceName = "Default";

    public SettingsWindow(
        AudioEngine audioEngine,
        ISettingsManager settingsManager,
        ILogger<SettingsWindow> logger)
    {
        _audioEngine = audioEngine;
        _settingsManager = settingsManager;
        _logger = logger;

        try
        {
            // Initialize component with error handling
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in InitializeComponent: {Message}", ex.Message);
                throw; // This is critical, so we need to throw
            }

            // Set DataContext safely
            try
            {
                DataContext = this;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting DataContext: {Message}", ex.Message);
            }

            // Register window placement safely
            try
            {
                ((App)Application.Current).WindowPlace.Register(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering window placement: {Message}", ex.Message);
            }

            // Add key event handler safely
            try
            {
                PreviewKeyDown += Window_PreviewKeyDown;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding key event handler: {Message}", ex.Message);
            }

            _logger.LogInformation("SettingsWindow initialized successfully");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error in SettingsWindow constructor: {Message}\n{StackTrace}",
                ex.Message, ex.StackTrace);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in SettingsWindow constructor: {Message}\n{StackTrace}",
                ex.Message, ex.StackTrace);
            throw;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Set theme with error handling
            try
            {
                int selectedThemeIndex = _themeManager.StringToThemeColorIndex(_settingsManager.Settings.SelectedTheme);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting theme: {Message}", ex.Message);
                try
                {
                    ThemesList.SelectedIndex = (int)ThemeColors.Dark;
                    _themeManager.ModifyTheme(ThemeColors.Dark);
                }
                catch (Exception themeEx)
                {
                    _logger.LogError(themeEx, "Error setting fallback theme: {Message}", themeEx.Message);
                }
            }

            // Set audio mode selection first
            OutputMode selectedOutputMode;
            try
            {

                selectedOutputMode = _audioEngine.GetCurrentOutputMode(); //_settingsManager.Settings.SelectedOutputMode;
                SetOutputModeSelection(selectedOutputMode);
                //_logger.LogInformation("Settings window loaded, audio mode UI set to: {OutputMode}", selectedOutputMode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting audio mode UI: {Message}", ex.Message);
                selectedOutputMode = OutputMode.DirectSound; // Safe fallback
            }

            // Load device list based on the current output mode
            try
            {
                RefreshDeviceListForMode(selectedOutputMode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading output devices: {Message}", ex.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error in Settings Window_Loaded: {Message}", ex.Message);
            // Don't rethrow - just log the error and continue
        }
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as ComboBox)?.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string themeName)
        {
            if (Enum.TryParse(themeName, out ThemeColors selectedTheme))
            {
                _themeManager.ModifyTheme(selectedTheme);
                //_logger.LogInformation("Theme changed to {Theme}", selectedTheme);
            }
            else
            {
                _logger.Log(LogLevel.Warning, "Failed to parse theme from ComboBoxItem Tag: {Tag}", themeName);
            }
        }
    }

    private void OnOutputModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as ComboBox)?.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string outputModeName)
        {
            if (Enum.TryParse(outputModeName, out OutputMode selectedOutputMode))
            {
                //_logger.Log(LogLevel.Information, "Audio mode changed to {OutputMode}", selectedOutputMode);

                // Update the device list when output mode changes
                RefreshDeviceListForMode(selectedOutputMode);
            }
            else
            {
                _logger.LogWarning("Failed to parse output mode from ComboBoxItem Tag: {Tag}", outputModeName);
            }
        }
    }

    private void RefreshDeviceListForMode(OutputMode outputMode)
    {
        try
        {
            OutputDeviceCombo.Items.Clear();

            IEnumerable<Device> devices = outputMode == OutputMode.DirectSound
                ? _audioEngine.DirectSoundDevices
                : _audioEngine.WasapiDevices;

            var deviceList = devices.ToList();
            //_logger.LogInformation("Refreshing device list for {OutputMode}: {Count} devices found", outputMode, deviceList.Count);

            // Populate ComboBox with strings only
            foreach (var device in deviceList)
            {
                OutputDeviceCombo.Items.Add(device.Name);
                //_logger.LogInformation("Added device to UI: '{DeviceName}'", device.Name);
            }

            //_logger.LogInformation("UI ComboBox now has {Count} items", OutputDeviceCombo.Items.Count);

            if (OutputDeviceCombo.Items.Count > 0)
            {
                string savedDeviceName = _settingsManager.Settings.SelectedOutputDevice?.Name ?? DefaultDeviceName;

                string deviceNameToSelect = deviceList.Any(d => d.Name == savedDeviceName)
                    ? savedDeviceName
                    : deviceList.First().Name;

                OutputDeviceCombo.SelectedItem = deviceNameToSelect;
                //_logger.LogInformation("Selected device: '{DeviceName}'", deviceNameToSelect);
            }
            else
            {
                _logger.LogWarning("No devices found for {OutputMode} mode!", outputMode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing device list: {Message}", ex.Message);
        }
    }

    private bool HandleOutputModeChange(out OutputMode selectedOutputMode)
    {
        selectedOutputMode = _settingsManager.Settings.SelectedOutputMode;
        bool changed = false;

        if (OutputModeCombo.SelectedItem is ComboBoxItem selectedItem &&
            selectedItem.Tag is string tag &&
            Enum.TryParse(tag, out OutputMode newMode) &&
            newMode != _settingsManager.Settings.SelectedOutputMode)
        {
            _settingsManager.Settings.SelectedOutputMode = newMode;
            _settingsManager.SaveSettings(nameof(AppSettings.SelectedOutputMode));
            selectedOutputMode = newMode;
            changed = true;
            //_logger.LogInformation("Audio mode setting changed to {OutputMode}", newMode);
        }
        return changed;
    }

    private bool HandleDeviceChange(out Device selectedDevice)
    {
        // Use strings in the ComboBox; map back to Device only for engine/settings
        string selectedName = (OutputDeviceCombo.SelectedItem as string) ?? DefaultDeviceName;
        
        // Determine current mode (HandleOutputModeChange is called before this)
        OutputMode currentMode = _settingsManager.Settings.SelectedOutputMode;

        IEnumerable<Device> devices = currentMode == OutputMode.DirectSound
            ? _audioEngine.DirectSoundDevices
            : _audioEngine.WasapiDevices;

        // Find the actual device object with the correct index
        selectedDevice = devices.FirstOrDefault(d => d.Name == selectedName)
            ?? new Device(DefaultDeviceName, OutputDeviceType.DirectSound, -1, true);

        bool changed = false;
        if (selectedName != (_settingsManager.Settings.SelectedOutputDevice?.Name ?? DefaultDeviceName))
        {
            // Save the actual device object, not a placeholder
            _settingsManager.Settings.SelectedOutputDevice = selectedDevice;
            _settingsManager.SaveSettings(nameof(AppSettings.SelectedOutputDevice));
            changed = true;
            //_logger.LogInformation("Audio device setting changed to {Device}", selectedName);
        }

        return changed;
    }

    private void HandleThemeChange()
    {
        string newTheme = _themeManager.IndexToThemeColorString(ThemesList.SelectedIndex);
        if (newTheme != _settingsManager.Settings.SelectedTheme)
        {
            _settingsManager.Settings.SelectedTheme = newTheme;
            _settingsManager.SaveSettings(nameof(AppSettings.SelectedTheme));
            //_logger.LogInformation("Theme setting changed to {Theme}", newTheme);
        }
    }

    private void ApplyAudioSettings(bool outputModeChanged, bool deviceChanged, OutputMode newMode, Device newDevice)
    {
        if (outputModeChanged || deviceChanged)
        {
            _audioEngine.SetOutputMode(newMode, newDevice);
           
            //if (newMode == OutputMode.DirectSound)
            //{
            //    _logger.LogInformation("DirectSound device changed to {Device}", newDevice.Name);
            //}
            //else
            //{
            //    _logger.LogInformation("WASAPI device changed to: {Device}", newDevice.Name);
            //}
        }
    }

    string _editedHotkey = "";
    private readonly Dictionary<string, string> _tempHotkeys = new();

    private void SetOutputModeSelection(OutputMode OutputMode)
    {
        try
        {
            if (OutputModeCombo?.Items == null)
            {
                _logger.LogWarning("OutputModeCombo or its Items is null");
                return;
            }

            for (int i = 0; i < OutputModeCombo.Items.Count; i++)
            {
                if (OutputModeCombo.Items[i] is ComboBoxItem item &&
                    item.Tag is string tag &&
                    Enum.TryParse<OutputMode>(tag, out var tagMode) &&
                    tagMode == OutputMode)
                {
                    OutputModeCombo.SelectedIndex = i;
                    return;
                }
            }

            // If no match found, select first item as fallback
            if (OutputModeCombo.Items.Count > 0)
            {
                OutputModeCombo.SelectedIndex = 0;
                _logger.LogWarning("Audio mode {OutputMode} not found in list, selected first item", OutputMode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting audio mode selection: {Message}", ex.Message);
        }
    }

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

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool outputModeChanged = HandleOutputModeChange(out OutputMode selectedOutputMode);
            bool deviceChanged = HandleDeviceChange(out Device selectedDevice);
            HandleThemeChange();

            ApplyAudioSettings(outputModeChanged, deviceChanged, selectedOutputMode, selectedDevice);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying settings: {Message}", ex.Message);
        }

        Hide();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Window? win = Window.GetWindow(this);
        if (win != null) win.Hide();
    }

    private void Window_Closing(object sender, EventArgs e)
    {
    }
}
