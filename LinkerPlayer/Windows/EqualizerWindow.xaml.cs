using LinkerPlayer.Core;
using LinkerPlayer.Models;
using MahApps.Metro.Controls;
using Serilog;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
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

    public EqualizerWindow()
    {
        InitializeComponent();
        WinMax.DoSourceInitialized(this);
        DataContext = this;

        EqualizerSettings.LoadFromJson();
        UpdateProfiles();

        EqSwitch.Switched += OnEqSwitched;

        EqSwitch.IsOn = Properties.Settings.Default.EqualizerOnStartEnabled;
    }

    public float Maximum => _mainWindow!.AudioStreamControl.MainMusic!.MaximumGain;

    public float Minimum => _mainWindow!.AudioStreamControl.MainMusic!.MinimumGain;

    private float GetBand(int index)
    {
        return _mainWindow!.AudioStreamControl.MainMusic!.GetBandGain(index);
    }

    private void SetBand(int index, float value)
    {
        _mainWindow!.AudioStreamControl.SetBandGain(index, value);
    }

    public float Band0
    {
        get => GetBand(0);
        set { SetBand(0, value); OnPropertyChanged(); }
    }

    public float Band1
    {
        get => GetBand(1);
        set { SetBand(1, value); OnPropertyChanged(); }
    }

    public float Band2
    {
        get => GetBand(2);
        set { SetBand(2, value); OnPropertyChanged(); }
    }

    public float Band3
    {
        get => GetBand(3);
        set { SetBand(3, value); OnPropertyChanged(); }
    }

    public float Band4
    {
        get => GetBand(4);
        set { SetBand(4, value); OnPropertyChanged(); }
    }

    public float Band5
    {
        get => GetBand(5);
        set { SetBand(5, value); OnPropertyChanged(); }
    }

    public float Band6
    {
        get => GetBand(6);
        set { SetBand(6, value); OnPropertyChanged(); }
    }

    public float Band7
    {
        get => GetBand(7);
        set { SetBand(7, value); OnPropertyChanged(); }
    }

    public float Band8
    {
        get => GetBand(8);
        set { SetBand(8, value); OnPropertyChanged(); }
    }

    public float Band9
    {
        get => GetBand(9);
        set { SetBand(9, value); OnPropertyChanged(); }
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (StartStopText.Content.Equals("Start"))
        {
            MainWindow? win = Owner as MainWindow;

            if (win!.AudioStreamControl.PathToMusic != null)
            {
                win!.AudioStreamControl.InitializeEqualizer();

                if (win.AudioStreamControl.MainMusic!.IsPlaying)
                {
                    win.AudioStreamControl.StopAndPlayFromPosition(win.AudioStreamControl.CurrentTrackPosition);
                }

                SliderSetEnabledState(true);
                ButtonsSetEnabledState(true);

                StartStopText.Content = "Stop";

                Profiles_SelectionChanged(null!, null!);
            }
        }
        else if (StartStopText.Content.Equals("Stop"))
        {
            MainWindow? win = Owner as MainWindow;

            win!.AudioStreamControl.StopEqualizer();

            if (win.AudioStreamControl.MainMusic!.IsPlaying)
            {
                win.AudioStreamControl.StopAndPlayFromPosition(win.AudioStreamControl.CurrentTrackPosition);
            }

            SliderSetEnabledState(false);
            ButtonsSetEnabledState(false);

            StartStopText.Content = "Start";

            ReloadButton_Click(null!, null!);
        }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
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

            bandsSettings!.EqualizerBands = _mainWindow!.AudioStreamControl.MainMusic!.GetBandsList();

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

            ReloadButton_Click(null!, null!);

            UpdateProfiles();

            _mainWindow!.SelectedBandsSettings = null!;

            Log.Information("Delete profile");

            EqualizerSettings.SaveToJson();
        }
        //}
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        //if (StartStopText.Content.Equals("Stop"))
        //{
        NamePopup.IsOpen = true;
        NamePopupTextBox.Focus();
        //}
    }

    private void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        //if (StartStopText.Content.Equals("Stop"))
        //{
        RenamePopup.IsOpen = true;
        ReNamePopupTextBox.Focus();
        //}
    }

    private void Profiles_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        if (StartStopText.Content.Equals("Stop"))
        {
            BandsSettings? bandsSettings = EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == Profiles.SelectedItem as String);

            if (bandsSettings != null)
            {
                _mainWindow.AudioStreamControl.SetBandsList(bandsSettings.EqualizerBands);

                _mainWindow.SelectedBandsSettings = bandsSettings;

                for (int i = 0; i < bandsSettings.EqualizerBands!.Count; i++)
                {
                    AnimationChangingSliderValue(i, bandsSettings.EqualizerBands![i].Gain);
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

    private void NamePopupTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string popupTextBoxText = NamePopupTextBox.Text.Trim();

            if (!string.IsNullOrEmpty(popupTextBoxText))
            {
                if (EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == popupTextBoxText) == null)
                {
                    BandsSettings bandsSettings = new BandsSettings
                    {
                        Name = popupTextBoxText,
                        EqualizerBands = _mainWindow!.AudioStreamControl.MainMusic!.GetBandsList()
                    };

                    EqualizerSettings.BandsSettings!.Add(bandsSettings);

                    Log.Information("New profile created");

                    EqualizerSettings.SaveToJson();

                    _mainWindow.SelectedBandsSettings = bandsSettings;

                    UpdateProfiles(bandsSettings.Name);
                }

                NamePopupTextBox.Text = "";
                NamePopup.IsOpen = false;
            }
        }
    }

    private void RenamePopupTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string popupTextBoxText = ReNamePopupTextBox.Text.Trim();

            if (!string.IsNullOrEmpty(popupTextBoxText))
            {
                BandsSettings? bandsSettings = EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == Profiles.SelectedItem as String);

                if (bandsSettings != null && EqualizerSettings.BandsSettings!.FirstOrDefault(n => n.Name == popupTextBoxText) == null)
                {
                    Log.Information($"Profile \"{bandsSettings.Name}\" was renamed to \"{popupTextBoxText}\"");

                    bandsSettings.Name = popupTextBoxText;

                    EqualizerSettings.SaveToJson();

                    _mainWindow!.SelectedBandsSettings = bandsSettings;

                    Log.Information("Profile has been selected");

                    UpdateProfiles(bandsSettings.Name);
                }


                ReNamePopupTextBox.Text = "";
                RenamePopup.IsOpen = false;
            }
        }
    }

    public void SliderSetEnabledState(bool state)
    {
        BandsSettings? bandsSettings = EqualizerSettings.BandsSettings!.FirstOrDefault();

        if (bandsSettings == null) { return; }

        //for (int i = 0; i < bandsSettings.EqualizerBands!.Count; i++)
        for (int i = 0; i < 10; i++)
        {
            Slider slider = (Slider)EqGrid.FindName($"Slider{i}")!;

            slider.IsEnabled = state;
        }
    }

    public void ButtonsSetEnabledState(bool state)
    {
        SaveButton.IsEnabled = state;
        DeleteButton.IsEnabled = state;
        RenameButton.IsEnabled = state;
        NameButton.IsEnabled = state;
        ReloadButton.IsEnabled = state;
    }

    private void AnimationChangingSliderValue(int index, float to)
    {
        SetBand(index, to);

        Slider slider = (Slider)EqGrid.FindName($"Slider{index}")!;

        DoubleAnimation doubleAnimation = new DoubleAnimation
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
            Slider slider = new Slider
            {
                Name = $"Slider{i}",
                Maximum = Maximum,
                Minimum = Minimum,
                Orientation = Orientation.Vertical,
                Style = (Style)FindResource("EqVerticalSlider"),
                TickFrequency = 1,
                TickPlacement = TickPlacement.BottomRight
            };

            Binding binding = new Binding
            {
                Path = new PropertyPath($"Band{i}"),
                Mode = BindingMode.TwoWay
            };

            slider.SetBinding(RangeBase.ValueProperty, binding);

            slider.HorizontalAlignment = HorizontalAlignment.Center;

            ColumnDefinition colDef = new ColumnDefinition();
            EqGrid.ColumnDefinitions.Add(colDef);

            EqGrid.Children.Add(slider);
            Grid.SetColumn(slider, i);

            EqGrid.RegisterName(slider.Name, slider);
        }

        Log.Information("Sliders was created");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = null!)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Window? win = Window.GetWindow(this);
        if (win != null) win.Close();
    }

    private void ButtonMouseEnter(object sender, MouseEventArgs e)
    {
        ((sender as Button)?.Content as Image)!.Opacity = 1;
    }

    private void ButtonMouseLeave(object sender, MouseEventArgs e)
    {
        (((sender as Button)?.Content as Image)!).Opacity = 0.6;
    }

    private void OnEqSwitched(object? sender, EventArgs e)
    {
        MainWindow? win = Owner as MainWindow;

        if (win == null) { return; }

        if (EqSwitch.IsOn)
        {
            if (win!.AudioStreamControl.PathToMusic != null)
            {
                win!.AudioStreamControl.InitializeEqualizer();

                //if (win.AudioStreamControl.MainMusic!.IsPlaying)
                //{
                //    win.AudioStreamControl.StopAndPlayFromPosition(win.AudioStreamControl.CurrentTrackPosition);
                //}

                SliderSetEnabledState(true);
                ButtonsSetEnabledState(true);

                StartStopText.Content = "Stop";

                Profiles_SelectionChanged(null!, null!);
            }
        }
        else
        {
            win!.AudioStreamControl.StopEqualizer();

            //if (win.AudioStreamControl.MainMusic!.IsPlaying)
            //{
            //    win.AudioStreamControl.StopAndPlayFromPosition(win.AudioStreamControl.CurrentTrackPosition);
            //}

            SliderSetEnabledState(false);
            ButtonsSetEnabledState(false);

            StartStopText.Content = "Start";

            ReloadButton_Click(null!, null!);
        }

        Properties.Settings.Default.EqualizerOnStartEnabled = EqSwitch.IsOn;
    }
}

