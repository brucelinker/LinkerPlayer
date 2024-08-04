using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using Serilog;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace LinkerPlayer.Windows;

public partial class EqualizerWindow : INotifyPropertyChanged
{
    private MainWindow? _mainWindow;
    private readonly AudioEngine _audioEngine;
    public BandsSettings SelectedEqualizerProfile = null!;

    public EqualizerWindow()
    {
        InitializeComponent();
        WinMax.DoSourceInitialized(this);
        DataContext = this;

        _audioEngine = AudioEngine.Instance;

        EqualizerSettings.LoadFromJson();
        UpdateProfiles();

        EqSwitch.Switched += OnEqSwitched;
        //EqSwitch.IsOn = true; // Properties.Settings.Default.EqualizerOnStartEnabled;
        //OnEqSwitched(null, EventArgs.Empty);
    }

    public float Maximum => _audioEngine.MaximumGain;

    public float Minimum => _audioEngine.MinimumGain;

    private float GetBand(int index)
    {
        return _audioEngine.GetBandGain(index);
    }

    private void SetBand(int index, float value)
    {
        _audioEngine.SetBandGain(index, value);
    }

    public float Band0
    {
        get => GetBand(0);
        set
        {
            SetBand(0, value); 
            OnPropertyChanged(null, new PropertyChangedEventArgs("Band0"));
        }
    }

    public float Band1
    {
        get => GetBand(1);
        set
        {
            SetBand(1, value); 
            OnPropertyChanged(null, new PropertyChangedEventArgs("Band1"));
        }
    }

    public float Band2
    {
        get => GetBand(2);
        set
        {
            SetBand(2, value); 
            OnPropertyChanged(null, new PropertyChangedEventArgs("Band2"));
        }
    }

    public float Band3
    {
        get => GetBand(3);
        set
        {
            SetBand(3, value); 
            OnPropertyChanged(null, new PropertyChangedEventArgs("Band3"));
        }
    }

    public float Band4
    {
        get => GetBand(4);
        set
        {
            SetBand(4, value); 
            OnPropertyChanged(null, new PropertyChangedEventArgs("Band4"));
        }
    }

    public float Band5
    {
        get => GetBand(5);
        set
        {
            SetBand(5, value); 
            OnPropertyChanged(null, new PropertyChangedEventArgs("Band5"));
        }
    }

    public float Band6
    {
        get => GetBand(6);
        set
        {
            SetBand(6, value); 
            OnPropertyChanged(null, new PropertyChangedEventArgs("Band6"));
        }
    }

    public float Band7
    {
        get => GetBand(7);
        set
        {
            SetBand(7, value); 
            OnPropertyChanged(null, new PropertyChangedEventArgs("Band7"));
        }
    }

    public float Band8
    {
        get => GetBand(8);
        set
        {
            SetBand(8, value); 
            OnPropertyChanged(null, new PropertyChangedEventArgs("Band8"));
        }
    }

    public float Band9
    {
        get => GetBand(9);
        set
        {
            SetBand(9, value); 
            OnPropertyChanged(null, new PropertyChangedEventArgs("Band9"));
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs propertyChangedEventArgs)
    {
        _audioEngine?.UpdateEqualizer();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
    }

    //private void StartStopButton_Click(object sender, RoutedEventArgs e)
    //{
    //    if (StartStopText.Content.Equals("Start"))
    //    {
    //        MainWindow? win = Owner as MainWindow;

    //        if (audioEngine.PathToMusic != null)
    //        {
    //            audioEngine.InitializeEqualizer();

    //            if (audioEngine.IsPlaying)
    //            {
    //                audioEngine.StopAndPlayFromPosition(audioEngine.CurrentTrackPosition);
    //            }

    //            SliderSetEnabledState(true);
    //            ButtonsSetEnabledState(true);

    //            StartStopText.Content = "Stop";

    //            Profiles_SelectionChanged(null!, null!);
    //        }
    //    }
    //    else if (StartStopText.Content.Equals("Stop"))
    //    {
    //        MainWindow? win = Owner as MainWindow;

    //        audioEngine.StopEqualizer();

    //        if (audioEngine.IsPlaying)
    //        {
    //            audioEngine.StopAndPlayFromPosition(audioEngine.CurrentTrackPosition);
    //        }

    //        SliderSetEnabledState(false);
    //        ButtonsSetEnabledState(false);

    //        StartStopText.Content = "Start";

    //        ResetButton_Click(null!, null!);
    //    }
    //}

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        for (int i = 0; i < 10; i++)
        {
            AnimationChangingSliderValue(i, 0);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        //if (StartStopText.Content.Equals("Stop"))
        //{
        if (!String.IsNullOrEmpty(Profiles.SelectedItem as String))
        {
            BandsSettings? bandsSettings = EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == Profiles.SelectedItem as String);

            bandsSettings!.EqualizerBands = _audioEngine.GetBandsList();

            EqualizerSettings.SaveToJson();
        }
        //}
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        //if (StartStopText.Content.Equals("Stop"))
        //{
        if (!String.IsNullOrEmpty(Profiles.SelectedItem as String))
        {
            EqualizerSettings.BandsSettings!.Remove(EqualizerSettings.BandsSettings.FirstOrDefault(n => n.Name == Profiles.SelectedItem as String)!);

            Profiles.SelectedItem = -1;

            ResetButton_Click(null!, null!);

            UpdateProfiles();

            //_mainWindow!.SelectedEqualizerProfile = null!;

            Log.Information("Delete profile");

            EqualizerSettings.SaveToJson();
        }
        //}
    }

    //private void AddButton_Click(object sender, RoutedEventArgs e)
    //{
        //if (StartStopText.Content.Equals("Stop"))
        //{
        //NamePopup.IsOpen = true;
        //NamePopupTextBox.Focus();
        //}
    //}

    //private void RenameButton_Click(object sender, RoutedEventArgs e)
    //{
    //    //if (StartStopText.Content.Equals("Stop"))
    //    //{
    //    RenamePopup.IsOpen = true;
    //    ReNamePopupTextBox.Focus();
    //    //}
    //}

    private void Profiles_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        if (EqSwitch.IsOn)
        {
            SelectedEqualizerProfile = EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == Profiles.SelectedItem as String)!;

            if (SelectedEqualizerProfile is { EqualizerBands: not null } && SelectedEqualizerProfile.EqualizerBands.Any())
            {
                _audioEngine.SetBandsList(SelectedEqualizerProfile.EqualizerBands);

                for (int i = 0; i < SelectedEqualizerProfile.EqualizerBands!.Count; i++)
                {
                    AnimationChangingSliderValue(i, SelectedEqualizerProfile.EqualizerBands![i].Gain);
                }

                Log.Information("Profile has been selected");
            }
        }
    }

    private void UpdateProfiles(string bandNameToSelect = null!)
    {
        Profiles.Items.Clear();

        foreach (BandsSettings profile in EqualizerSettings.BandsSettings!)
        {
            Profiles.Items.Add(profile.Name);
        }

        if (bandNameToSelect != null!)
        {
            Profiles.SelectedItem = bandNameToSelect;
        }
        else if (!Profiles.Items.IsEmpty)
        {
            Profiles.SelectedItem = Profiles.Items[0];
        }
    }

    public void LoadSelectedBand(BandsSettings bandsSettingsToLoad)
    {
        if (bandsSettingsToLoad != null!)
        {
            Profiles.SelectedItem = bandsSettingsToLoad.Name;
        }
    }

    //private void NamePopupTextBox_KeyDown(object sender, KeyEventArgs e)
    //{
    //    if (e.Key == Key.Enter)
    //    {
    //        string popupTextBoxText = NamePopupTextBox.Text.Trim();

    //        if (!string.IsNullOrEmpty(popupTextBoxText))
    //        {
    //            if (EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == popupTextBoxText) == null)
    //            {
    //                BandsSettings bandsSettings = new()
    //                {
    //                    Name = popupTextBoxText,
    //                    EqualizerBands = audioEngine.GetBandsList()
    //                };

    //                EqualizerSettings.BandsSettings!.Add(bandsSettings);

    //                Log.Information("New profile created");

    //                EqualizerSettings.SaveToJson();

    //                _mainWindow!.SelectedEqualizerProfile = bandsSettings;

    //                UpdateProfiles(bandsSettings.Name);
    //            }

    //            NamePopupTextBox.Text = "";
    //            NamePopup.IsOpen = false;
    //        }
    //    }
    //}

    //private void RenamePopupTextBox_KeyDown(object sender, KeyEventArgs e)
    //{
    //    if (e.Key == Key.Enter)
    //    {
    //        string popupTextBoxText = ReNamePopupTextBox.Text.Trim();

    //        if (!string.IsNullOrEmpty(popupTextBoxText))
    //        {
    //            BandsSettings? bandsSettings = EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == Profiles.SelectedItem as String);

    //            if (bandsSettings != null && EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == popupTextBoxText) == null)
    //            {
    //                Log.Information($"Profile \"{bandsSettings.Name}\" was renamed to \"{popupTextBoxText}\"");

    //                bandsSettings.Name = popupTextBoxText;

    //                EqualizerSettings.SaveToJson();

    //                _mainWindow!.SelectedEqualizerProfile = bandsSettings;

    //                Log.Information("Profile has been selected");

    //                UpdateProfiles(bandsSettings.Name);
    //            }


    //            ReNamePopupTextBox.Text = "";
    //            RenamePopup.IsOpen = false;
    //        }
    //    }
    //}

    private void SliderSetEnabledState(bool state)
    {
        BandsSettings? bandsSettings = EqualizerSettings.BandsSettings!.FirstOrDefault();

        if (bandsSettings == null) { return; }

        for (int i = 0; i < bandsSettings.EqualizerBands!.Count; i++)
        //for (int i = 0; i < 10; i++)
        {
            Slider? slider = (Slider?)EqGrid.FindName($"Slider{i}")!;
            slider.IsEnabled = state;
        }
    }

    private void ButtonsSetEnabledState(bool state)
    {
        SaveButton.IsEnabled = state;
        DeleteButton.IsEnabled = state;
        ResetButton.IsEnabled = state;
    }

    private void AnimationChangingSliderValue(int index, float to)
    {
        SetBand(index, to);

        Slider slider = (Slider)EqGrid.FindName($"Slider{index}")!;

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
        };
        doubleAnimation.EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut };

        slider.BeginAnimation(RangeBase.ValueProperty, doubleAnimation);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _mainWindow = (Owner as MainWindow)!;

        //BandsSettings? bandsSettings = EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == Profiles.SelectedItem as String);
        BandsSettings? bandsSettings = EqualizerSettings.BandsSettings!.FirstOrDefault();

        if (bandsSettings == null) { return; }

        for (int i = 0; i < 10; i++)
        {
            Slider slider = new()
            {
                Name = $"Slider{i}",
                Maximum = Maximum,
                Minimum = Minimum,
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

        SliderSetEnabledState(true);
        ButtonsSetEnabledState(true);

        Log.Information("Sliders was created");
    }

    public void Window_Closed(object sender, EventArgs e)
    {
        if (Properties.Settings.Default.EqualizerOnStartEnabled)
        {
            Properties.Settings.Default.EqualizerProfileName =
                SelectedEqualizerProfile != null! ? SelectedEqualizerProfile.Name : null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;


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

                SliderSetEnabledState(true);
                ButtonsSetEnabledState(true);

                Profiles_SelectionChanged(null!, null!);
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

            ResetButton_Click(null!, null!);
        }

        Properties.Settings.Default.EqualizerOnStartEnabled = EqSwitch.IsOn;
    }
}

