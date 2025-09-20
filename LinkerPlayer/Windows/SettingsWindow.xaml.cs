using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly OutputDeviceManager _outputDeviceManager;
    private readonly ILogger _logger;
    private static readonly SettingsManager SettingsManager = App.AppHost.Services.GetRequiredService<SettingsManager>();

    private const string DefaultDeviceName = "Default";

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

            // Initialize component with error handling
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, "Error in InitializeComponent: {Message}", ex.Message);
                throw; // This is critical, so we need to throw
            }

            // Set DataContext safely
            try
            {
                DataContext = this;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, "Error setting DataContext: {Message}", ex.Message);
            }

            // Register window placement safely
            try
            {
                ((App)Application.Current).WindowPlace.Register(this);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, "Error registering window placement: {Message}", ex.Message);
            }

            // Add key event handler safely
            try
            {
                PreviewKeyDown += Window_PreviewKeyDown;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, "Error adding key event handler: {Message}", ex.Message);
            }

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
        try
        {
            _logger.Log(LogLevel.Information, "Settings Window_Loaded starting");

            // Set theme with error handling
            try
            {
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
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, "Error setting theme: {Message}", ex.Message);
                try
                {
                    ThemesList.SelectedIndex = (int)ThemeColors.Dark;
                    _themeManager.ModifyTheme(ThemeColors.Dark);
                }
                catch (Exception themeEx)
                {
                    _logger.Log(LogLevel.Error, themeEx, "Error setting fallback theme: {Message}", themeEx.Message);
                }
            }

            // Set audio mode selection first
            OutputMode selectedOutputMode;
            try
            {
                selectedOutputMode = SettingsManager.Settings.SelectedOutputMode;
                SetOutputModeSelection(selectedOutputMode);
                _logger.Log(LogLevel.Information, "Settings window loaded, audio mode UI set to: {OutputMode}", selectedOutputMode);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, "Error setting audio mode UI: {Message}", ex.Message);
                selectedOutputMode = OutputMode.DirectSound; // Safe fallback
            }

            // Load device list based on the current output mode
            try
            {
                RefreshDeviceListForMode(selectedOutputMode);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, "Error loading output devices: {Message}", ex.Message);
            }

            _logger.Log(LogLevel.Information, "Settings Window_Loaded completed successfully");
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, ex, "Critical error in Settings Window_Loaded: {Message}", ex.Message);
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
                _logger.Log(LogLevel.Information, "Theme changed to {Theme}", selectedTheme);
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
                _logger.Log(LogLevel.Information, "Audio mode changed to {OutputMode}", selectedOutputMode);

                // Update the device list when output mode changes
                RefreshDeviceListForMode(selectedOutputMode);
            }
            else
            {
                _logger.Log(LogLevel.Warning, "Failed to parse output mode from ComboBoxItem Tag: {Tag}", outputModeName);
            }
        }
    }

    private void RefreshDeviceListForMode(OutputMode outputMode)
    {
        try
        {
            OutputDeviceCombo.Items.Clear();

            IEnumerable<Device> devices = outputMode == OutputMode.DirectSound
                ? _outputDeviceManager.GetDirectSoundDevices()
                : _outputDeviceManager.GetWasapiDevices();

            var deviceList = devices.ToList();
            _logger.Log(LogLevel.Information, "Refreshing device list for {OutputMode}: {Count} devices found", outputMode, deviceList.Count);

            // Populate ComboBox with strings only
            foreach (var device in deviceList)
            {
                OutputDeviceCombo.Items.Add(device.Name);
                _logger.Log(LogLevel.Information, "Added device to UI: '{DeviceName}'", device.Name);
            }

            _logger.Log(LogLevel.Information, "UI ComboBox now has {Count} items", OutputDeviceCombo.Items.Count);

            if (OutputDeviceCombo.Items.Count > 0)
            {
                string savedDeviceName = SettingsManager.Settings.SelectedOutputDevice ?? DefaultDeviceName;

                string deviceNameToSelect = deviceList.Any(d => d.Name == savedDeviceName)
                    ? savedDeviceName
                    : deviceList.First().Name;

                OutputDeviceCombo.SelectedItem = deviceNameToSelect;
                _logger.Log(LogLevel.Information, "Selected device: '{DeviceName}'", deviceNameToSelect);
            }
            else
            {
                _logger.Log(LogLevel.Warning, "No devices found for {OutputMode} mode!", outputMode);
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, ex, "Error refreshing device list: {Message}", ex.Message);
        }
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
            _logger.Log(LogLevel.Error, ex, "Error applying settings: {Message}", ex.Message);
        }

        Hide();
    }

    private bool HandleOutputModeChange(out OutputMode selectedOutputMode)
    {
        selectedOutputMode = SettingsManager.Settings.SelectedOutputMode;
        bool changed = false;

        if (OutputModeCombo.SelectedItem is ComboBoxItem selectedItem &&
            selectedItem.Tag is string tag &&
            Enum.TryParse(tag, out OutputMode newMode) &&
            newMode != SettingsManager.Settings.SelectedOutputMode)
        {
            SettingsManager.Settings.SelectedOutputMode = newMode;
            SettingsManager.SaveSettings(nameof(AppSettings.SelectedOutputMode));
            selectedOutputMode = newMode;
            changed = true;
            _logger.Log(LogLevel.Information, "Audio mode setting changed to {OutputMode}", newMode);
        }
        return changed;
    }

    private bool HandleDeviceChange(out Device selectedDevice)
    {
        // Use strings in the ComboBox; map back to Device only for engine/settings
        string selectedName = (OutputDeviceCombo.SelectedItem as string) ?? DefaultDeviceName;
        bool changed = false;

        if (selectedName != (SettingsManager.Settings.SelectedOutputDevice ?? DefaultDeviceName))
        {
            SettingsManager.Settings.SelectedOutputDevice = selectedName;
            SettingsManager.SaveSettings(nameof(AppSettings.SelectedOutputDevice));
            changed = true;
            _logger.Log(LogLevel.Information, "Audio device setting changed to {Device}", selectedName);
        }

        // Determine current mode (HandleOutputModeChange is called before this)
        OutputMode currentMode = SettingsManager.Settings.SelectedOutputMode;

        IEnumerable<Device> devices = currentMode == OutputMode.DirectSound
            ? _outputDeviceManager.GetDirectSoundDevices()
            : _outputDeviceManager.GetWasapiDevices();

        selectedDevice = devices.FirstOrDefault(d => d.Name == selectedName)
            ?? new Device(DefaultDeviceName, DeviceType.DirectSound, -1, true);

        return changed;
    }

    private void HandleThemeChange()
    {
        string newTheme = _themeManager.IndexToThemeColorString(ThemesList.SelectedIndex);
        if (newTheme != SettingsManager.Settings.SelectedTheme)
        {
            SettingsManager.Settings.SelectedTheme = newTheme;
            SettingsManager.SaveSettings(nameof(AppSettings.SelectedTheme));
            _logger.Log(LogLevel.Information, "Theme setting changed to {Theme}", newTheme);
        }
    }

    private void ApplyAudioSettings(bool outputModeChanged, bool deviceChanged, OutputMode newMode, Device newDevice)
    {
        if (outputModeChanged)
        {
            _audioEngine.ChangeOutputMode(newMode);
            _logger.Log(LogLevel.Information, "Audio engine mode changed to {OutputMode}", newMode);
        }
        else if (deviceChanged)
        {
            if (newMode == OutputMode.DirectSound)
            {
                _audioEngine.ReselectOutputDevice(newDevice);
                _logger.Log(LogLevel.Information, "DirectSound device changed to {Device}", newDevice.Name);
            }
            else
            {
                _logger.Log(LogLevel.Information, "WASAPI device changed - re-initializing audio engine with new device: {Device}", newDevice.Name);
                _audioEngine.ChangeOutputMode(newMode);
            }
        }
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

    private OutputMode StringToOutputMode(string? OutputModeString)
    {
        if (string.IsNullOrEmpty(OutputModeString))
        {
            return OutputMode.DirectSound; // Safe default
        }

        return OutputModeString switch
        {
            "DirectSound" => OutputMode.DirectSound,
            "WASAPI Shared" => OutputMode.WasapiShared,
            "WASAPI Exclusive" => OutputMode.WasapiExclusive,
            _ => OutputMode.DirectSound // Safe default for unknown values
        };
    }

    private string OutputModeToString(OutputMode OutputMode)
    {
        return OutputMode switch
        {
            OutputMode.DirectSound => "DirectSound",
            OutputMode.WasapiShared => "WASAPI Shared",
            OutputMode.WasapiExclusive => "WASAPI Exclusive",
            _ => "WASAPI Shared"
        };
    }

    private void SetOutputModeSelection(OutputMode OutputMode)
    {
        try
        {
            if (OutputModeCombo?.Items == null)
            {
                _logger.Log(LogLevel.Warning, "OutputModeCombo or its Items is null");
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
                _logger.Log(LogLevel.Warning, "Audio mode {OutputMode} not found in list, selected first item", OutputMode);
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, ex, "Error setting audio mode selection: {Message}", ex.Message);
        }
    }
}
