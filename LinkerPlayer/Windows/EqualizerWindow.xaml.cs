using LinkerPlayer.Core;
using LinkerPlayer.Models;
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
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace LinkerPlayer.Windows;

public partial class EqualizerWindow : INotifyPropertyChanged
{
    public EqualizerWindow()
    {
        InitializeComponent();
        WinMax.DoSourceInitialized(this);
        DataContext = this;

        EqualizerLibrary.LoadFromJson();
        UpdateProfiles();
    }

    public float Maximum => (Owner as MainWindow)!.AudioStreamControl.MainMusic!.MaximumGain;

    public float Minimum => (Owner as MainWindow)!.AudioStreamControl.MainMusic!.MinimumGain;

    private float GetBand(int index)
    {
        return (Owner as MainWindow)!.AudioStreamControl.MainMusic!.GetBandGain(index);
    }

    private void SetBand(int index, float value)
    {
        (Owner as MainWindow)!.AudioStreamControl.SetBandGain(index, value);
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

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            Uri uri = new Uri("/Images/restore.png", UriKind.Relative);
            ImageSource imgSource = new BitmapImage(uri);
            TitlebarButtons.MaximizeButtonImage.Source = imgSource;

        }
        else if (WindowState == WindowState.Normal)
        {
            Uri uri = new Uri("/Images/maximize.png", UriKind.Relative);
            ImageSource imgSource = new BitmapImage(uri);
            TitlebarButtons.MaximizeButtonImage.Source = imgSource;
        }
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (StartStopText.Text == "Start")
        {
            MainWindow? win = Owner as MainWindow;

            if (win!.AudioStreamControl.PathToMusic != null)
            {
                win.AudioStreamControl.InitializeEqualizer();

                if (win.AudioStreamControl.MainMusic!.IsPlaying)
                {
                    win.AudioStreamControl.StopAndPlayFromPosition(win.AudioStreamControl.CurrentTrackPosition);
                }

                SliderSetEnabledState(true);
                ButtonsSetEnabledState(true);

                StartStopText.Text = "Stop";

                Profiles_SelectionChanged(null!, null!);
            }
        }
        else if (StartStopText.Text == "Stop")
        {
            MainWindow? win = Owner as MainWindow;

            win!.AudioStreamControl.StopEqualizer();

            if (win.AudioStreamControl.MainMusic!.IsPlaying)
            {
                win.AudioStreamControl.StopAndPlayFromPosition(win.AudioStreamControl.CurrentTrackPosition);
            }

            SliderSetEnabledState(false);
            ButtonsSetEnabledState(false);

            StartStopText.Text = "Start";

            ReloadButton_Click(null!, null!);
        }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        for (int i = 0; i < 8; i++)
        {
            AnimationChangingSliderValue(i, 0);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (StartStopText.Text == "Stop")
        {
            if (!String.IsNullOrEmpty(Profiles.SelectedItem as String))
            {
                BandsSettings? band = EqualizerLibrary.BandsSettings!.FirstOrDefault(n => n.Name == Profiles.SelectedItem as String);

                band!.EqualizerBands = (Owner as MainWindow)!.AudioStreamControl.MainMusic!.GetBandsList();

                EqualizerLibrary.SaveToJson();
            }
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (StartStopText.Text == "Stop")
        {
            if (!String.IsNullOrEmpty(Profiles.SelectedItem as String))
            {
                EqualizerLibrary.BandsSettings!.Remove(EqualizerLibrary.BandsSettings.FirstOrDefault(n => n.Name == Profiles.SelectedItem as String)!);

                Profiles.SelectedItem = -1;

                ReloadButton_Click(null!, null!);

                UpdateProfiles();

                (Owner as MainWindow)!.SelectedBandsSettings = null!;

                Log.Information("Delete profile");

                EqualizerLibrary.SaveToJson();
            }
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (StartStopText.Text == "Stop")
        {
            NamePopup.IsOpen = true;
            NamePopupTextBox.Focus();
        }
    }

    private void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        if (StartStopText.Text == "Stop")
        {
            ReNamePopup.IsOpen = true;
            ReNamePopupTextBox.Focus();
        }
    }

    private void Profiles_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (StartStopText.Text == "Stop")
        {
            BandsSettings? band = EqualizerLibrary.BandsSettings!.FirstOrDefault(n => n.Name == Profiles.SelectedItem as String);

            if (band != null)
            {
                (Owner as MainWindow)!.AudioStreamControl.SetBandsList(band.EqualizerBands);

                (Owner as MainWindow)!.SelectedBandsSettings = band;

                for (int i = 0; i < 8; i++)
                {
                    AnimationChangingSliderValue(i, band.EqualizerBands![i].Gain);
                }

                Log.Information("Profile has been selected");
            }
        }
    }

    private void UpdateProfiles(string bandNameToSelect = null!)
    {
        Profiles.Items.Clear();

        foreach (BandsSettings profile in EqualizerLibrary.BandsSettings!)
        {
            Profiles.Items.Add(profile.Name);
        }

        if (bandNameToSelect != null!)
        {
            Profiles.SelectedItem = bandNameToSelect;
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
                if (EqualizerLibrary.BandsSettings!.FirstOrDefault(n => n.Name == popupTextBoxText) == null)
                {
                    BandsSettings band = new BandsSettings
                    {
                        Name = popupTextBoxText,
                        EqualizerBands = (Owner as MainWindow)!.AudioStreamControl.MainMusic!.GetBandsList()
                    };

                    EqualizerLibrary.BandsSettings!.Add(band);

                    Log.Information("New profile created");

                    EqualizerLibrary.SaveToJson();

                    (Owner as MainWindow)!.SelectedBandsSettings = band;

                    UpdateProfiles(band.Name);
                }

                NamePopupTextBox.Text = "";
                NamePopup.IsOpen = false;
            }
        }
    }

    private void ReNamePopupTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string popupTextBoxText = ReNamePopupTextBox.Text.Trim();

            if (!string.IsNullOrEmpty(popupTextBoxText))
            {
                BandsSettings? band = EqualizerLibrary.BandsSettings!.FirstOrDefault(n => n.Name == Profiles.SelectedItem as String);

                if (band != null && EqualizerLibrary.BandsSettings!.FirstOrDefault(n => n.Name == popupTextBoxText) == null)
                {
                    Log.Information($"Profile \"{band.Name}\" was renamed to \"{popupTextBoxText}\"");

                    band.Name = popupTextBoxText;

                    EqualizerLibrary.SaveToJson();

                    (Owner as MainWindow)!.SelectedBandsSettings = band;

                    Log.Information("Profile has been selected");

                    UpdateProfiles(band.Name);
                }


                ReNamePopupTextBox.Text = "";
                ReNamePopup.IsOpen = false;
            }
        }
    }

    public void SliderSetEnabledState(bool state)
    {
        for (int i = 0; i < 8; i++)
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

        doubleAnimation.Completed += (_, _) => {
            slider.BeginAnimation(RangeBase.ValueProperty, null);
            slider.Value = GetBand(index);
        };
        doubleAnimation.EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut };

        slider.BeginAnimation(RangeBase.ValueProperty, doubleAnimation);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        for (int i = 0; i <= 7; i++)
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
}
