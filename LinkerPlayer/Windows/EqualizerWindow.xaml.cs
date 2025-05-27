using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace LinkerPlayer.Windows;

public partial class EqualizerWindow
{
    private readonly AudioEngine _audioEngine;
    private BandsSettings? _selectedPreset;
    private const string FlatPreset = "Flat";
    private readonly EqualizerViewModel _equalizerViewModel = new();
    private readonly SettingsManager _settingsManager;

    public EqualizerWindow()
    {
        InitializeComponent();
        WinMax.DoSourceInitialized(this);
        DataContext = _equalizerViewModel;

        _audioEngine = AudioEngine.Instance;

        _equalizerViewModel.LoadFromJson();
        UpdatePresets();

        EqSwitch.Switched += OnEqSwitched;
        this.Closed += Window_Closed!;

        _settingsManager = App.AppHost!.Services.GetRequiredService<SettingsManager>();
        EqSwitch.IsOn = _settingsManager.Settings.EqualizerEnabled;
        ;

        WeakReferenceMessenger.Default.Register<MainWindowClosingMessage>(this, (_, _) =>
        {
            OnMainWindowClosing();
        });
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
            _audioEngine.SetBandGain(index, value);
        }
    }

    private string FormatLabel(float value)
    {
        return value > 0 ? value.ToString("+0.0") : value.ToString("0.0");
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        NewPopup.IsOpen = true;
        NewPopupTextBox.Focus();

        if (!string.IsNullOrEmpty(Presets.SelectedItem as string))
        {
            BandsSettings? bandsSettings = _equalizerViewModel.BandsSettings!.FirstOrDefault(n => n.Name == Presets.SelectedItem as string);

            bandsSettings!.EqualizerBands = _audioEngine.GetBandsList();

            _equalizerViewModel.SaveToJson();
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Presets.SelectedItem as string))
        {
            BandsSettings? bandsSettings = _equalizerViewModel.BandsSettings!.FirstOrDefault(n => n.Name == Presets.SelectedItem as string);

            bandsSettings!.EqualizerBands = _audioEngine.GetBandsList();

            _equalizerViewModel.SaveToJson();
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Presets.SelectedItem as string))
        {
            _equalizerViewModel.BandsSettings!.Remove(_equalizerViewModel.BandsSettings.FirstOrDefault(n => n.Name == Presets.SelectedItem as string)!);

            Presets.SelectedItem = 0;

            ResetSliders();

            UpdatePresets();

            _selectedPreset = null!;

            Log.Information("Delete preset");

            _equalizerViewModel.SaveToJson();
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

        UpdatePresets(FlatPreset);
    }

    private void Presets_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (EqSwitch.IsOn)
        {
            _selectedPreset = _equalizerViewModel.BandsSettings!.FirstOrDefault(n => n.Name == Presets.SelectedItem as string)!;

            if (_selectedPreset is { EqualizerBands: not null } && _selectedPreset.EqualizerBands.Any())
            {
                _audioEngine.SetBandsList(_selectedPreset.EqualizerBands);

                for (int i = 0; i < _selectedPreset.EqualizerBands!.Count; i++)
                {
                    AnimationChangingSliderValue(i, _selectedPreset.EqualizerBands![i].Gain);
                }

                ButtonsSetEnabledState(true);

                Log.Information("Profile has been selected");
            }
        }
    }

    private void UpdatePresets(string bandNameToSelect = null!)
    {
        Presets.Items.Clear();

        foreach (BandsSettings preset in _equalizerViewModel.BandsSettings!)
        {
            Presets.Items.Add(preset.Name);
        }

        if (bandNameToSelect != null!)
        {
            Presets.SelectedItem = bandNameToSelect;
        }
        else if (!Presets.Items.IsEmpty)
        {
            Presets.SelectedItem = Presets.Items[0];
        }
    }

    private void SliderSetEnabledState(bool state)
    {
        BandsSettings? bandsSettings = _equalizerViewModel.BandsSettings!.FirstOrDefault();

        if (bandsSettings == null) { return; }

        for (int i = 0; i < bandsSettings.EqualizerBands!.Count; i++)
        {
            Slider slider = (Slider)EqGrid.FindName($"Slider{i}")!;
            slider.IsEnabled = state;
        }
    }

    private void ButtonsSetEnabledState(bool state)
    {
        if (_selectedPreset == null) { return; }
        NewButton.IsEnabled = state;
        SaveButton.IsEnabled = !_selectedPreset.Locked && state;
        DeleteButton.IsEnabled = !_selectedPreset.Locked && state;
        ResetButton.IsEnabled = state;
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
        BandsSettings? bandsSettings = _equalizerViewModel.BandsSettings!.FirstOrDefault();

        if (bandsSettings == null) { return; }

        Presets.SelectedItem = Properties.Settings.Default.EqualizerProfileName;
        _selectedPreset = _equalizerViewModel.BandsSettings!.FirstOrDefault(n => n.Name == Presets.SelectedItem as string)!;

        EqSwitch.IsOn = Properties.Settings.Default.EqualizerOnStartEnabled;

        SliderSetEnabledState(EqSwitch.IsOn);
        ButtonsSetEnabledState(EqSwitch.IsOn);

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

    // CloseBox button
    private void ButtonMouseEnter(object sender, MouseEventArgs e)
    {
        ((sender as Button)?.Content as Image)!.Opacity = 1;
    }

    // CloseBox button
    private void ButtonMouseLeave(object sender, MouseEventArgs e)
    {
        (((sender as Button)?.Content as Image)!).Opacity = 0.6;
    }

    private void OnEqSwitched(object? sender, EventArgs e)
    {
        WeakReferenceMessenger.Default.Send(new EqualizerIsOnMessage(EqSwitch.IsOn));

        if (EqSwitch.IsOn)
        {
            if (_audioEngine.PathToMusic != null)
            {
                _audioEngine.InitializeEqualizer();

                if (_audioEngine.IsPlaying)
                {
                    _audioEngine.StopAndPlayFromPosition(_audioEngine.CurrentTrackPosition);
                }

                Presets_SelectionChanged(null!, null!);

                SliderSetEnabledState(true);
                ButtonsSetEnabledState(true);
            }
        }
        else
        {
            if (_audioEngine.IsPlaying)
            {
                _audioEngine.StopAndPlayFromPosition(_audioEngine.CurrentTrackPosition);
            }

            SliderSetEnabledState(false);
            ButtonsSetEnabledState(false);

            ResetSliders();
        }

        _settingsManager.Settings.EqualizerEnabled = EqSwitch.IsOn;
        _settingsManager.SaveSettings(nameof(AppSettings.EqualizerEnabled));
    }

    private void NewPopupTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string popupTextBoxText = NewPopupTextBox.Text.Trim();

            if (!string.IsNullOrEmpty(popupTextBoxText))
            {
                if (_equalizerViewModel.BandsSettings!.FirstOrDefault(n => n.Name == popupTextBoxText) == null)
                {
                    BandsSettings bandsSettings = new()
                    {
                        Name = popupTextBoxText,
                        EqualizerBands = _audioEngine.GetBandsList()
                    };

                    _equalizerViewModel.BandsSettings!.Add(bandsSettings);

                    Log.Information("New preset created");

                    _equalizerViewModel.SaveToJson();

                    _selectedPreset = bandsSettings;

                    UpdatePresets(bandsSettings.Name);
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
}