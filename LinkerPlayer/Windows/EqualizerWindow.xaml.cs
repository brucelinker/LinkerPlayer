using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace LinkerPlayer.Windows;

[ObservableObject]
public partial class EqualizerWindow
{
    [ObservableProperty] private string _selectedPresetName = FlatPreset;
    private Preset? _selectedPreset;
    private const string FlatPreset = "Flat";

    private readonly EqualizerViewModel _equalizerViewModel;
    private readonly AudioEngine _audioEngine;
    private readonly SettingsManager _settingsManager;
    private readonly ILogger<EqualizerWindow> _logger;

    public EqualizerWindow(
        EqualizerViewModel viewModel,
        AudioEngine audioEngine,
        SettingsManager settingsManager,
        ILogger<EqualizerWindow> logger)
    {
        _logger = logger;
        try
        {
            _logger.Log(LogLevel.Information, "Initializing EqualizerWindow");
            InitializeComponent();
            WinMax.DoSourceInitialized(this);
            DataContext = _equalizerViewModel;

            DataContext = viewModel;
            _equalizerViewModel = viewModel;

            _audioEngine = audioEngine;

            _equalizerViewModel.LoadFromJson();

            EqSwitch.Switched += OnEqSwitched;
            this.Closed += Window_Closed!;

            _settingsManager = settingsManager;
            _logger = logger;
            EqSwitch.IsOn = _settingsManager.Settings.EqualizerEnabled;
            UpdatePresetsComboBox(_settingsManager.Settings.EqualizerPresetName);
            Preset? preset = _equalizerViewModel.EqPresets!
                .FirstOrDefault(n => n.Name == _settingsManager.Settings.EqualizerPresetName);

            ApplyPreset(preset ?? _equalizerViewModel.EqPresets[0]);

            WeakReferenceMessenger.Default.Register<MainWindowClosingMessage>(this, (_, _) =>
            {
                OnMainWindowClosing();
            });
            _logger.Log(LogLevel.Information, "EqualizerWindow initialized successfully");
        }
        catch (IOException ex)
        {
            _logger.Log(LogLevel.Error, ex, "IO error in EqualizerWindow constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, ex, "Unexpected error in EqualizerWindow constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
    }

    private void OnEqSwitched(object? sender, EventArgs e)
    {
        _audioEngine.EqEnabled = EqSwitch.IsOn;

        if (EqSwitch.IsOn)
        {
            ControlsSetEnabledState(true);

            if (_audioEngine.IsPlaying)
            {
                _audioEngine.StopAndPlayFromPosition(_audioEngine.CurrentTrackPosition);
            }
        }
        else
        {
            ControlsSetEnabledState(false);

            if (_audioEngine.IsPlaying)
            {
                _audioEngine.StopAndPlayFromPosition(_audioEngine.CurrentTrackPosition);
            }
        }

        _settingsManager.Settings.EqualizerEnabled = EqSwitch.IsOn;
        _settingsManager.SaveSettings(nameof(AppSettings.EqualizerEnabled));
    }

    private void Presets_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (EqSwitch.IsOn)
        {
            _selectedPreset =
                _equalizerViewModel.EqPresets!.FirstOrDefault(n => n.Name == Presets_ComboBox.SelectedItem as string)!;

            ApplyPreset(_selectedPreset);
        }
    }

    private void ApplyPreset(Preset preset)
    {

        if (preset is { EqualizerBands: not null } && preset.EqualizerBands.Any())
        {
            _audioEngine.SetBandsList(preset.EqualizerBands);

            for (int i = 0; i < preset.EqualizerBands!.Count; i++)
            {
                AnimationChangingSliderValue(i, preset.EqualizerBands![i].Gain);
            }

            // We only want to save the preset name if the switch is on
            if (EqSwitch.IsOn)
            {
                _settingsManager.Settings.EqualizerPresetName = preset.Name!;
                _settingsManager.SaveSettings(nameof(AppSettings.EqualizerPresetName));
            }

            Log.Information("Profile has been selected");
        }
    }

    private void UpdatePresetsComboBox(string presetNameToSelect = null!)
    {
        Presets_ComboBox.Items.Clear();

        foreach (Preset preset in _equalizerViewModel.EqPresets!)
        {
            Presets_ComboBox.Items.Add(preset.Name);
        }

        if (presetNameToSelect != null!)
        {
            Presets_ComboBox.SelectedItem = presetNameToSelect;
        }
        else if (!Presets_ComboBox.Items.IsEmpty)
        {
            Presets_ComboBox.SelectedItem = FlatPreset;
        }
    }

    private void SetBand(int index, float value)
    {
        TextBlock? label = (TextBlock?)EqGrid.FindName($"Band{index}Label");
        if (label != null)
        {
            label.Text = FormatLabel(value);
        }

        if (_audioEngine.IsEqualizerInitialized)
        {
            _audioEngine.SetBandGainByIndex(index, value);
        }
    }

    private string FormatLabel(float value)
    {
        return value > 0 ? value.ToString("+0.0") : value.ToString("0.0");
    }

    private void NewPopupTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string popupTextBoxText = NewPopupTextBox.Text.Trim();

            if (!string.IsNullOrEmpty(popupTextBoxText))
            {
                if (_equalizerViewModel.EqPresets!.FirstOrDefault(n => n.Name == popupTextBoxText) == null)
                {
                    Preset bandsSettings = new()
                    {
                        Name = popupTextBoxText,
                        EqualizerBands = _audioEngine.GetBandsList()
                    };

                    _equalizerViewModel.EqPresets!.Add(bandsSettings);

                    Log.Information("New preset created");

                    _equalizerViewModel.SaveEqPresets();

                    _selectedPreset = bandsSettings;

                    UpdatePresetsComboBox(bandsSettings.Name);
                }

                NewPopupTextBox.Text = "";
                NewPopup.IsOpen = false;
            }
        }

        if (e.Key == Key.Escape)
        {
            NewPopupTextBox.Text = "";
            NewPopup.IsOpen = false;
        }
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        NewPopup.IsOpen = true;
        NewPopupTextBox.Focus();

        if (!string.IsNullOrEmpty(Presets_ComboBox.SelectedItem as string))
        {
            Preset? preset = _equalizerViewModel.EqPresets!.FirstOrDefault(n => n.Name == _selectedPreset!.Name);

            if (preset != null)
            {
                preset.EqualizerBands = _audioEngine.GetBandsList();

                _equalizerViewModel.EqPresets!.Add(preset);

                Log.Information("New preset created");

                _equalizerViewModel.SaveEqPresets();

                _selectedPreset = preset;

                UpdatePresetsComboBox(preset.Name!);
            }
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Presets_ComboBox.SelectedItem as string))
        {
            Preset preset = _equalizerViewModel.EqPresets!.FirstOrDefault(n => n.Name == _selectedPreset!.Name)!;

            preset!.EqualizerBands = _audioEngine.GetBandsList();

            _equalizerViewModel.SaveEqPresets();

            UpdatePresetsComboBox(preset.Name!);
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Presets_ComboBox.SelectedItem as string))
        {
            _equalizerViewModel.EqPresets!.Remove(_equalizerViewModel.EqPresets.FirstOrDefault(n => n.Name == Presets_ComboBox.SelectedItem as string)!);

            Presets_ComboBox.SelectedItem = 0;

            ResetSliders();

            UpdatePresetsComboBox();

            _selectedPreset = null!;

            Log.Information("Delete preset");

            _equalizerViewModel.SaveEqPresets();
        }
    }

    private void ResetSliders()
    {
        for (int i = 0; i < 10; i++)
        {
            AnimationChangingSliderValue(i, 0);
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        ResetSliders();

        UpdatePresetsComboBox(FlatPreset);
    }

    private void ControlsSetEnabledState(bool state)
    {
        if (_selectedPreset == null) { return; }
        Presets_ComboBox.IsEnabled = state;
        NewButton.IsEnabled = state;
        SaveButton.IsEnabled = !_selectedPreset.Locked && state;
        DeleteButton.IsEnabled = !_selectedPreset.Locked && state;
        ResetButton.IsEnabled = state;

        Preset? presets = _equalizerViewModel.EqPresets!.FirstOrDefault();

        if (presets == null) { return; }

        for (int i = 0; i < presets.EqualizerBands!.Count; i++)
        {
            Slider slider = (Slider)EqGrid.FindName($"Slider{i}")!;
            slider.IsEnabled = state;
        }
    }

    private void AnimationChangingSliderValue(int index, float to)
    {
        SetBand(index, to);

        Slider slider = (Slider)EqGrid.FindName($"Slider{index}")!;
        TextBlock label = (TextBlock)EqGrid.FindName($"Band{index}Label")!;

        DoubleAnimation doubleAnimation = new()
        {
            From = slider.Value,
            To = to,
            Duration = TimeSpan.FromMilliseconds(500)
        };

        doubleAnimation.Completed += (_, _) =>
        {
            slider.BeginAnimation(RangeBase.ValueProperty, null);
            slider.Value = _audioEngine.GetBandGain(index);
            label.Text = FormatLabel((float)slider.Value);
        };
        doubleAnimation.EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut };

        slider.BeginAnimation(RangeBase.ValueProperty, doubleAnimation);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Preset? bandsSettings = _equalizerViewModel.EqPresets!.FirstOrDefault();

        if (bandsSettings == null) { return; }

        Presets_ComboBox.SelectedItem = Properties.Settings.Default.EqualizerProfileName;
        _selectedPreset = _equalizerViewModel.EqPresets!.FirstOrDefault(n => n.Name == Presets_ComboBox.SelectedItem as string)!;

        EqSwitch.IsOn = Properties.Settings.Default.EqualizerOnStartEnabled;

        Log.Information("Sliders was created");
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        if (Properties.Settings.Default.EqualizerOnStartEnabled)
        {
            Properties.Settings.Default.EqualizerProfileName =
                _selectedPreset != null! ? _selectedPreset.Name : null;
        }
    }

    private void OnMainWindowClosing()
    {
        Window? win = GetWindow(this);

        win?.Close();
    }

    // CloseBox and CloseButton
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Window? win = GetWindow(this);

        win?.Hide();
    }
}