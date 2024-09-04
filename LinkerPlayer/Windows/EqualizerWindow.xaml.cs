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
        TextBlock? label = (TextBlock?)EqGrid.FindName($"Band{index}Label");
        if (label != null)
        {
            label.Text = FormatLabel(value);
        }

        _audioEngine.SetBandGain(index, value);
    }

    public float Band0
    {
        get => GetBand(0);
        set
        {
            SetBand(0, value); 
            OnPropertyChanged();
        }
    }

    public float Band1
    {
        get => GetBand(1);
        set
        {
            SetBand(1, value); 
            OnPropertyChanged();
        }
    }

    public float Band2
    {
        get => GetBand(2);
        set
        {
            SetBand(2, value); 
            OnPropertyChanged();
        }
    }

    public float Band3
    {
        get => GetBand(3);
        set
        {
            SetBand(3, value); 
            OnPropertyChanged();
        }
    }

    public float Band4
    {
        get => GetBand(4);
        set
        {
            SetBand(4, value); 
            OnPropertyChanged();
        }
    }

    public float Band5
    {
        get => GetBand(5);
        set
        {
            SetBand(5, value); 
            OnPropertyChanged();
        }
    }

    public float Band6
    {
        get => GetBand(6);
        set
        {
            SetBand(6, value); 
            OnPropertyChanged();
        }
    }

    public float Band7
    {
        get => GetBand(7);
        set
        {
            SetBand(7, value); 
            OnPropertyChanged();
        }
    }

    public float Band8
    {
        get => GetBand(8);
        set
        {
            SetBand(8, value); 
            OnPropertyChanged();
        }
    }

    public float Band9
    {
        get => GetBand(9);
        set
        {
            SetBand(9, value); 
            OnPropertyChanged();
        }
    }

    public string Band0Value
    {
        get { return this.FormatLabel(this.Band0); }
    }

    public string Band1Value
    {
        get { return this.FormatLabel(this.Band1); }
    }

    public string Band2Value
    {
        get { return this.FormatLabel(this.Band2); }
    }

    public string Band3Value
    {
        get { return this.FormatLabel(this.Band3); }
    }

    public string Band4Value
    {
        get { return this.FormatLabel(this.Band4); }
    }

    public string Band5Value
    {
        get { return this.FormatLabel(this.Band5); }
    }

    public string Band6Value
    {
        get { return this.FormatLabel(this.Band6); }
    }

    public string Band7Value
    {
        get { return this.FormatLabel(this.Band7); }
    }

    public string Band8Value
    {
        get { return this.FormatLabel(this.Band8); }
    }

    public string Band9Value
    {
        get { return this.FormatLabel(this.Band9); }
    }


    private void OnPropertyChanged()
    {
        _audioEngine.UpdateEqualizer();
    }

    private string FormatLabel(float value)
    {
        return value > 0 ? value.ToString("+0.0") : value.ToString("0.0");
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

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        NewPopup.IsOpen = true;
        NewPopupTextBox.Focus();

        //if (!String.IsNullOrEmpty(Profiles.SelectedItem as String))
        //{
        //    BandsSettings? bandsSettings = EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == Profiles.SelectedItem as String);

        //    bandsSettings!.EqualizerBands = _audioEngine.GetBandsList();

        //    EqualizerSettings.SaveToJson();
        //}
    }


    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        //SavePopup.IsOpen = true;
        //SavePopupTextBox.Focus();

        if (!String.IsNullOrEmpty(Profiles.SelectedItem as String))
        {
            BandsSettings? bandsSettings = EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == Profiles.SelectedItem as String);

            bandsSettings!.EqualizerBands = _audioEngine.GetBandsList();

            EqualizerSettings.SaveToJson();
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        //if (StartStopText.Content.Equals("Stop"))
        //{
        if (!String.IsNullOrEmpty(Profiles.SelectedItem as String))
        {
            EqualizerSettings.BandsSettings!.Remove(EqualizerSettings.BandsSettings.FirstOrDefault(n => n.Name == Profiles.SelectedItem as String)!);

            Profiles.SelectedItem = 0;

            ResetButton_Click(null!, null!);

            UpdateProfiles();

            SelectedEqualizerProfile = null!;

            Log.Information("Delete profile");

            EqualizerSettings.SaveToJson();
        }
        //}
    }
    
    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        for (int i = 0; i < 10; i++)
        {
            AnimationChangingSliderValue(i, 0);
        }
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

                ButtonsSetEnabledState(true);

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
            Slider slider = (Slider)EqGrid.FindName($"Slider{i}")!;
            slider.IsEnabled = state;
        }
    }

    private void ButtonsSetEnabledState(bool state)
    {
        NewButton.IsEnabled = state;
        SaveButton.IsEnabled = !SelectedEqualizerProfile.Locked && state;
        DeleteButton.IsEnabled = !SelectedEqualizerProfile.Locked && state;
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

        Profiles.SelectedItem = Properties.Settings.Default.EqualizerProfileName;
        SelectedEqualizerProfile = EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == Profiles.SelectedItem as String)!;

        EqSwitch.IsOn = Properties.Settings.Default.EqualizerOnStartEnabled;

        SliderSetEnabledState(EqSwitch.IsOn);
        ButtonsSetEnabledState(EqSwitch.IsOn);

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

                Profiles_SelectionChanged(null!, null!);

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

            ResetButton_Click(null!, null!);
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

                    Log.Information("New profile created");

                    EqualizerSettings.SaveToJson();

                    SelectedEqualizerProfile = bandsSettings;

                    UpdateProfiles(bandsSettings.Name);
                }

                NewPopupTextBox.Text = "";
                NewPopup.IsOpen = false;
            }
        }
    }
}

