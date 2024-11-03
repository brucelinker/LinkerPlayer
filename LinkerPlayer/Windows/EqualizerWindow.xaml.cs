using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using Serilog;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using LinkerPlayer.ViewModels;

namespace LinkerPlayer.Windows;

public partial class EqualizerWindow
{
    private readonly AudioEngine _audioEngine;
    private readonly EqualizerViewModel _equalizerViewModel;
    private BandsSettings _selectedEqualizerProfile = null!;
    private const string FlatPreset = "Flat";

    public EqualizerWindow()
    {
        InitializeComponent();
        WinMax.DoSourceInitialized(this);
        _equalizerViewModel = new EqualizerViewModel();
        DataContext = _equalizerViewModel;

        _audioEngine = AudioEngine.Instance;

        EqualizerSettings.LoadFromJson();
        UpdatePresets();

        EqSwitch.Switched += OnEqSwitched;
        this.Closed += Window_Closed!;
    }

    private float GetBand(int index)
    {
        return _audioEngine.GetBandGain(index);
    }

    private void SetBand(int index, float value)
    {
        TextBlock? label = (TextBlock?)EqGrid.FindName($"Band{index}Label");
        if (label != null)
        {
            label.Text = FormatLabel(value);
        }

        _audioEngine.SetBandGain(index, value);
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
            BandsSettings? bandsSettings = EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == Presets.SelectedItem as string);

            bandsSettings!.EqualizerBands = _audioEngine.GetBandsList();

            EqualizerSettings.SaveToJson();
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Presets.SelectedItem as string))
        {
            BandsSettings? bandsSettings = EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == Presets.SelectedItem as string);

            bandsSettings!.EqualizerBands = _audioEngine.GetBandsList();

            EqualizerSettings.SaveToJson();
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Presets.SelectedItem as string))
        {
            EqualizerSettings.BandsSettings!.Remove(EqualizerSettings.BandsSettings.FirstOrDefault(n => n.Name == Presets.SelectedItem as string)!);

            Presets.SelectedItem = 0;

            ResetSliders();

            UpdatePresets();

            _selectedEqualizerProfile = null!;

            Log.Information("Delete preset");

            EqualizerSettings.SaveToJson();
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
            _selectedEqualizerProfile = EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == Presets.SelectedItem as string)!;

            if (_selectedEqualizerProfile is { EqualizerBands: not null } && _selectedEqualizerProfile.EqualizerBands.Any())
            {
                _audioEngine.SetBandsList(_selectedEqualizerProfile.EqualizerBands);

                for (int i = 0; i < _selectedEqualizerProfile.EqualizerBands!.Count; i++)
                {
                    AnimationChangingSliderValue(i, _selectedEqualizerProfile.EqualizerBands![i].Gain);
                }

                ButtonsSetEnabledState(true);

                Log.Information("Profile has been selected");
            }
        }
    }

    private void UpdatePresets(string bandNameToSelect = null!)
    {
        Presets.Items.Clear();

        foreach (BandsSettings preset in EqualizerSettings.BandsSettings!)
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
        BandsSettings? bandsSettings = EqualizerSettings.BandsSettings!.FirstOrDefault();

        if (bandsSettings == null) { return; }

        for (int i = 0; i < bandsSettings.EqualizerBands!.Count; i++)
        //for (int i = 0; i < 10; i++)
        {
            Slider slider = (Slider)EqGrid.FindName($"Slider{i}")!;
            slider.IsEnabled = state;
        }
    }

    private void ButtonsSetEnabledState(bool state)
    {
        NewButton.IsEnabled = state;
        SaveButton.IsEnabled = !_selectedEqualizerProfile.Locked && state;
        DeleteButton.IsEnabled = !_selectedEqualizerProfile.Locked && state;
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
            slider.Value = GetBand(index);
            label.Text = FormatLabel((float)slider.Value); // $"{slider.Value}";
        };
        doubleAnimation.EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut };

        slider.BeginAnimation(RangeBase.ValueProperty, doubleAnimation);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        //_mainWindow = (Owner as MainWindow)!;

        BandsSettings? bandsSettings = EqualizerSettings.BandsSettings!.FirstOrDefault();

        if (bandsSettings == null) { return; }

        for (int i = 0; i < 10; i++)
        {
            Slider slider = new()
            {
                Name = $"Slider{i}",
                Maximum = _equalizerViewModel.MaximumGain,
                Minimum = _equalizerViewModel.MinimumGain,
                Orientation = Orientation.Vertical,
                Style = (Style)FindResource("EqVerticalSlider"),
                TickFrequency = 1,
                TickPlacement = TickPlacement.BottomRight
            };

            Binding binding = new()
            {
                Path = new PropertyPath($"Band{i}"),
                Mode = BindingMode.TwoWay
            };

            slider.SetBinding(RangeBase.ValueProperty, binding);

            slider.HorizontalAlignment = HorizontalAlignment.Center;

            ColumnDefinition colDef = new();
            EqGrid.ColumnDefinitions.Add(colDef);

            EqGrid.Children.Add(slider);
            Grid.SetColumn(slider, i);

            EqGrid.RegisterName(slider.Name, slider);
        }

        Presets.SelectedItem = Properties.Settings.Default.EqualizerProfileName;
        _selectedEqualizerProfile = EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == Presets.SelectedItem as string)!;

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
                _selectedEqualizerProfile != null! ? _selectedEqualizerProfile.Name : null;
        }
    }

    // CloseBox and CloseButton
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Window? win = GetWindow(this);
        if (win != null) win.Close();
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
            //_audioEngine.StopEqualizer();

            if (_audioEngine.IsPlaying)
            {
                _audioEngine.StopAndPlayFromPosition(_audioEngine.CurrentTrackPosition);
            }

            SliderSetEnabledState(false);
            ButtonsSetEnabledState(false);

            ResetSliders();
        }

        Properties.Settings.Default.EqualizerOnStartEnabled = EqSwitch.IsOn;
    }

    private void NewPopupTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string popupTextBoxText = NewPopupTextBox.Text.Trim();

            if (!string.IsNullOrEmpty(popupTextBoxText))
            {
                if (EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == popupTextBoxText) == null)
                {
                    BandsSettings bandsSettings = new()
                    {
                        Name = popupTextBoxText,
                        EqualizerBands = _audioEngine.GetBandsList()
                    };

                    EqualizerSettings.BandsSettings!.Add(bandsSettings);

                    Log.Information("New preset created");

                    EqualizerSettings.SaveToJson();

                    _selectedEqualizerProfile = bandsSettings;

                    UpdatePresets(bandsSettings.Name);
                }

                NewPopupTextBox.Text = "";
                NewPopup.IsOpen = false;
            }
        }
    }
}

