using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LinkerPlayer.Windows;

public partial class SettingsWindow
{
    private readonly ThemeManager _themeManager = new();
    private readonly IAudioEngine _audioEngine;
    private readonly ISettingsManager _settingsManager;
    private readonly IWatchedFolderService _watchedFolderService;
    private readonly IMusicLibrary _musicLibrary;
    private readonly ILogger _logger;

    private const string DefaultDeviceName = "Primary Sound Driver";
    private bool _isLoaded = false;

    private sealed class PendingBehaviorSettings
    {
        public bool CrossfadeEnabled { get; set; }
        public int CrossfadeFadeInMs { get; set; }
        public int CrossfadeFadeOutMs { get; set; }
        public FadeCurveShape CrossfadeCurveShape { get; set; }

        public bool SkipSilenceEnabled { get; set; }
        public int SkipSilenceMinimumDurationMs { get; set; }
        public int SkipSilenceThresholdDb { get; set; }
    }

    private PendingBehaviorSettings _pendingBehavior = new PendingBehaviorSettings();

    public SettingsWindow(
        IAudioEngine audioEngine,
        ISettingsManager settingsManager,
        IWatchedFolderService watchedFolderService,
        IMusicLibrary musicLibrary,
        ILogger<SettingsWindow> logger)
    {
        _audioEngine = audioEngine;
        _settingsManager = settingsManager;
        _watchedFolderService = watchedFolderService;
        _musicLibrary = musicLibrary;
        _logger = logger;

        try
        {
            ((App)Application.Current).WindowPlace.Register(this, "SettingsWindow");

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
            LoadBehaviorSettings();

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

            // ItemTemplate is defined in XAML to avoid runtime Visual reuse issues.

            ShowPage(0);
            NavigationListBox.SelectedIndex = 0;

            // Now it's safe to attach the event handler
            NavigationListBox.SelectionChanged += Navigation_SelectionChanged;

            _isLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error in Settings Window_Loaded: {Message}", ex.Message);
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

            List<Device> deviceList = devices.ToList();

            foreach (Device device in deviceList)
            {
                OutputDeviceCombo.Items.Add(device);
            }

            if (OutputDeviceCombo.Items.Count > 0)
            {
                Device savedDevice = _settingsManager.Settings.SelectedOutputDevice ?? new Device(DefaultDeviceName, OutputDeviceType.DirectSound, -1, true);

                Device? deviceToSelect = deviceList.FirstOrDefault(d => d.Type == savedDevice.Type && d.Index == savedDevice.Index)
                    ?? deviceList.FirstOrDefault(d => string.Equals(d.Name, savedDevice.Name, StringComparison.Ordinal))
                    ?? deviceList.First();

                OutputDeviceCombo.SelectedItem = deviceToSelect;
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
        selectedDevice = (OutputDeviceCombo.SelectedItem as Device)
            ?? new Device(DefaultDeviceName, OutputDeviceType.DirectSound, -1, true);

        bool changed = false;

        Device savedDevice = _settingsManager.Settings.SelectedOutputDevice
            ?? new Device(DefaultDeviceName, OutputDeviceType.DirectSound, -1, true);

        if (selectedDevice.Type != savedDevice.Type || selectedDevice.Index != savedDevice.Index)
        {
            _settingsManager.Settings.SelectedOutputDevice = selectedDevice;
            _settingsManager.SaveSettings(nameof(AppSettings.SelectedOutputDevice));
            changed = true;
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

    private static bool IsKnownProblemDevice(OutputMode mode, Device device)
    {
        // Based on observed behavior (also matches MusicBee):
        // - "Speakers (USB Audio Device)" can run (meters move) but be silent in DirectSound and WASAPI.
        if (device.Name.Contains("USB Audio Device", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string GetKnownProblemDeviceMessage(OutputMode mode, Device device)
    {
        if (device.Name.Contains("USB Audio Device", StringComparison.OrdinalIgnoreCase))
        {
            return $"'{device.Name}' may produce no sound with {mode} on some systems (meters may still move). Try a different device or switch output mode.";
        }

        return $"'{device.Name}' may not work correctly with {mode}.";
    }

    private void WarnIfKnownProblemDevice(OutputMode mode, Device device)
    {
        try
        {
            if (!IsKnownProblemDevice(mode, device))
            {
                return;
            }

            MessageBox.Show(
                GetKnownProblemDeviceMessage(mode, device),
                "Audio Device Warning",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
        }
    }

    private void ApplyAudioSettings(bool outputModeChanged, bool deviceChanged, OutputMode newMode, Device newDevice)
    {
        if (outputModeChanged || deviceChanged)
        {
            WarnIfKnownProblemDevice(newMode, newDevice);
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
                    Enum.TryParse<OutputMode>(tag, out OutputMode tagMode) &&
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

    private void Navigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded)
            return; // Extra safety, though not needed after attaching post-load

        if (sender is ListBox listBox && listBox.SelectedIndex >= 0)
        {
            ShowPage(listBox.SelectedIndex);
        }
    }

    private void ShowPage(int index)
    {
        // Hide all
        OutputPage.Visibility = Visibility.Collapsed;
        AppearancePage.Visibility = Visibility.Collapsed;
        BehaviorPage.Visibility = Visibility.Collapsed;
        LibraryPage.Visibility = Visibility.Collapsed;

        // Show selected
        switch (index)
        {
            case 0:
                OutputPage.Visibility = Visibility.Visible;
                break;
            case 1:
                AppearancePage.Visibility = Visibility.Visible;
                break;
            case 2:
                BehaviorPage.Visibility = Visibility.Visible;
                break;
            case 3:
                LibraryPage.Visibility = Visibility.Visible;
                LoadLibrarySettings();
                break;
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

            ApplyBehaviorSettings();

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
        if (win != null)
        {
            win.Hide();
        }
    }

    private void Window_Closing(object sender, EventArgs e)
    {
    }

    private void LoadBehaviorSettings()
    {
        try
        {
            _pendingBehavior = new PendingBehaviorSettings
            {
                CrossfadeEnabled = _settingsManager.Settings.CrossfadeEnabled,
                CrossfadeFadeInMs = _settingsManager.Settings.CrossfadeFadeInMs,
                CrossfadeFadeOutMs = _settingsManager.Settings.CrossfadeFadeOutMs,
                CrossfadeCurveShape = _settingsManager.Settings.CrossfadeCurveShape,
                SkipSilenceEnabled = _settingsManager.Settings.SkipSilenceEnabled,
                SkipSilenceMinimumDurationMs = _settingsManager.Settings.SkipSilenceMinimumDurationMs,
                SkipSilenceThresholdDb = _settingsManager.Settings.SkipSilenceThresholdDb,
            };

            if (CrossfadeEnabledCheckBox != null)
            {
                CrossfadeEnabledCheckBox.IsChecked = _pendingBehavior.CrossfadeEnabled;
            }

            if (CrossfadeFadeInSlider != null)
            {
                CrossfadeFadeInSlider.Value = _pendingBehavior.CrossfadeFadeInMs;
            }
            if (CrossfadeFadeOutSlider != null)
            {
                CrossfadeFadeOutSlider.Value = _pendingBehavior.CrossfadeFadeOutMs;
            }

            if (CrossfadeFadeInCurveCombo != null)
            {
                CrossfadeFadeInCurveCombo.SelectedItem = FindFadeCurveComboItem(CrossfadeFadeInCurveCombo, _pendingBehavior.CrossfadeCurveShape);
            }
            if (CrossfadeFadeOutCurveCombo != null)
            {
                CrossfadeFadeOutCurveCombo.SelectedItem = FindFadeCurveComboItem(CrossfadeFadeOutCurveCombo, _pendingBehavior.CrossfadeCurveShape);
            }

            if (SkipSilenceEnabledCheckBox != null)
            {
                SkipSilenceEnabledCheckBox.IsChecked = _pendingBehavior.SkipSilenceEnabled;
            }
            if (SkipSilenceMinDurationSlider != null)
            {
                SkipSilenceMinDurationSlider.Value = _pendingBehavior.SkipSilenceMinimumDurationMs;
            }
            if (SkipSilenceThresholdSlider != null)
            {
                SkipSilenceThresholdSlider.Value = _pendingBehavior.SkipSilenceThresholdDb;
            }

            UpdateBehaviorTextFields();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading behavior settings");
        }
    }

    private static ComboBoxItem? FindFadeCurveComboItem(ComboBox comboBox, FadeCurveShape curve)
    {
        foreach (object item in comboBox.Items)
        {
            if (item is ComboBoxItem comboItem && comboItem.Content is string content)
            {
                if (string.Equals(content, curve.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return comboItem;
                }
            }
        }
        return null;
    }

    private void UpdateBehaviorTextFields()
    {
        if (CrossfadeFadeInValueText != null && CrossfadeFadeInSlider != null)
        {
            CrossfadeFadeInValueText.Text = ((int)CrossfadeFadeInSlider.Value).ToString();
        }
        if (CrossfadeFadeOutValueText != null && CrossfadeFadeOutSlider != null)
        {
            CrossfadeFadeOutValueText.Text = ((int)CrossfadeFadeOutSlider.Value).ToString();
        }
        if (SkipSilenceMinDurationValueText != null && SkipSilenceMinDurationSlider != null)
        {
            SkipSilenceMinDurationValueText.Text = ((int)SkipSilenceMinDurationSlider.Value).ToString();
        }
        if (SkipSilenceThresholdValueText != null && SkipSilenceThresholdSlider != null)
        {
            SkipSilenceThresholdValueText.Text = ((int)SkipSilenceThresholdSlider.Value).ToString();
        }
    }

    private void ApplyBehaviorSettings()
    {
        bool crossfadeEnabled = CrossfadeEnabledCheckBox?.IsChecked == true;
        int fadeInMs = CrossfadeFadeInSlider != null ? (int)CrossfadeFadeInSlider.Value : _settingsManager.Settings.CrossfadeFadeInMs;
        int fadeOutMs = CrossfadeFadeOutSlider != null ? (int)CrossfadeFadeOutSlider.Value : _settingsManager.Settings.CrossfadeFadeOutMs;

        FadeCurveShape curveShape = _settingsManager.Settings.CrossfadeCurveShape;
        if (CrossfadeFadeInCurveCombo?.SelectedItem is ComboBoxItem fadeInCurveItem && fadeInCurveItem.Content is string fadeInCurveText && Enum.TryParse(fadeInCurveText, out FadeCurveShape parsedIn))
        {
            curveShape = parsedIn;
        }
        if (CrossfadeFadeOutCurveCombo?.SelectedItem is ComboBoxItem fadeOutCurveItem && fadeOutCurveItem.Content is string fadeOutCurveText && Enum.TryParse(fadeOutCurveText, out FadeCurveShape parsedOut))
        {
            curveShape = parsedOut;
        }

        bool skipSilenceEnabled = SkipSilenceEnabledCheckBox?.IsChecked == true;
        int skipMinMs = SkipSilenceMinDurationSlider != null ? (int)SkipSilenceMinDurationSlider.Value : _settingsManager.Settings.SkipSilenceMinimumDurationMs;
        int skipThresholdDb = SkipSilenceThresholdSlider != null ? (int)SkipSilenceThresholdSlider.Value : _settingsManager.Settings.SkipSilenceThresholdDb;

        _settingsManager.Settings.CrossfadeEnabled = crossfadeEnabled;
        _settingsManager.Settings.CrossfadeFadeInMs = fadeInMs;
        _settingsManager.Settings.CrossfadeFadeOutMs = fadeOutMs;
        _settingsManager.Settings.CrossfadeCurveShape = curveShape;

        _settingsManager.Settings.SkipSilenceEnabled = skipSilenceEnabled;
        _settingsManager.Settings.SkipSilenceMinimumDurationMs = skipMinMs;
        _settingsManager.Settings.SkipSilenceThresholdDb = skipThresholdDb;

        _settingsManager.SaveSettings(nameof(AppSettings.CrossfadeEnabled));
        _settingsManager.SaveSettings(nameof(AppSettings.CrossfadeFadeInMs));
        _settingsManager.SaveSettings(nameof(AppSettings.CrossfadeFadeOutMs));
        _settingsManager.SaveSettings(nameof(AppSettings.CrossfadeCurveShape));
        _settingsManager.SaveSettings(nameof(AppSettings.SkipSilenceEnabled));
        _settingsManager.SaveSettings(nameof(AppSettings.SkipSilenceMinimumDurationMs));
        _settingsManager.SaveSettings(nameof(AppSettings.SkipSilenceThresholdDb));
    }

    private void OnCrossfadeFadeInSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateBehaviorTextFields();
    }

    private void OnCrossfadeFadeOutSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateBehaviorTextFields();
    }

    private void OnSkipSilenceMinDurationSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateBehaviorTextFields();
    }

    private void OnSkipSilenceThresholdSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateBehaviorTextFields();
    }

    private void LoadLibrarySettings()
    {
        WatchedFoldersListBox.Items.Clear();
        foreach (string folder in _watchedFolderService.WatchedFolders)
        {
            WatchedFoldersListBox.Items.Add(folder);
        }
    }

    private void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.OpenFolderDialog dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select a folder to watch for audio files",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            _watchedFolderService.AddFolder(dialog.FolderName);
            LoadLibrarySettings();
            _ = ScanFolderAsync(dialog.FolderName);
        }
    }

    private async void OnRemoveFolderClick(object sender, RoutedEventArgs e)
    {
        if (WatchedFoldersListBox.SelectedItem is not string selectedFolder)
            return;

        // Count tracks from this folder so the user knows what they're about to affect
        int trackCount = _musicLibrary.MainLibrary
            .Count(t => t.Path.StartsWith(
                selectedFolder.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                    + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));

        string trackSummary = trackCount > 0
            ? $"{trackCount:N0} track{(trackCount == 1 ? "" : "s")} from this folder {(trackCount == 1 ? "is" : "are")} in your library."
            : "No tracks from this folder are currently in your library.";

        MessageBoxResult result = MessageBox.Show(
            $"{trackSummary}\n\nWhat would you like to do?\n\n" +
            "• Yes  — Remove folder and delete its tracks from the library\n" +
            "• No   — Remove folder only (keep tracks in library)\n" +
            "• Cancel — Do nothing\n\n" +
            "Note: your actual audio files on disk will NOT be deleted.",
            "Remove Watched Folder",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
            return;

        RemoveFolderButton.IsEnabled = false;
        try
        {
            if (result == MessageBoxResult.Yes && trackCount > 0)
            {
                WeakReferenceMessenger.Default.Send(new ProgressValueMessage(new ProgressData
                {
                    IsProcessing = true,
                    TotalTracks = trackCount,
                    ProcessedTracks = 0,
                    Status = $"Removing {trackCount:N0} tracks from library…",
                    Phase = "Removing"
                }));
                int removed = await Task.Run(() => _watchedFolderService.RemoveFolderAndTracksAsync(selectedFolder))
                    .ConfigureAwait(false);
                WeakReferenceMessenger.Default.Send(new ProgressValueMessage(new ProgressData
                {
                    IsProcessing = false,
                    TotalTracks = removed,
                    ProcessedTracks = removed,
                    Status = $"Removed {removed:N0} track{(removed == 1 ? "" : "s")} from library.",
                    Phase = string.Empty
                }));
            }
            else
            {
                _watchedFolderService.RemoveFolder(selectedFolder);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing watched folder: {Path}", selectedFolder);
        }
        finally
        {
            Dispatcher.Invoke(() =>
            {
                RemoveFolderButton.IsEnabled = true;
                LoadLibrarySettings();
            });
        }
    }

    private async Task ScanFolderAsync(string folderPath)
    {
        AddFolderButton.IsEnabled = false;
        RescanButton.IsEnabled = false;
        try
        {
            IProgress<ProgressData> progress = new Progress<ProgressData>(data =>
                WeakReferenceMessenger.Default.Send(new ProgressValueMessage(data)));
            await _watchedFolderService.ScanFolderAsync(folderPath, progress).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning new folder: {Path}", folderPath);
        }
        finally
        {
            Dispatcher.Invoke(() =>
            {
                AddFolderButton.IsEnabled = true;
                RescanButton.IsEnabled = true;
            });
        }
    }

    private async void OnRescanClick(object sender, RoutedEventArgs e)
    {
        AddFolderButton.IsEnabled = false;
        RescanButton.IsEnabled = false;
        try
        {
            IProgress<ProgressData> progress = new Progress<ProgressData>(data =>
                WeakReferenceMessenger.Default.Send(new ProgressValueMessage(data)));
            await _watchedFolderService.ScanAllAsync(progress).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during manual rescan");
        }
        finally
        {
            Dispatcher.Invoke(() =>
            {
                AddFolderButton.IsEnabled = true;
                RescanButton.IsEnabled = true;
            });
        }
    }
}
