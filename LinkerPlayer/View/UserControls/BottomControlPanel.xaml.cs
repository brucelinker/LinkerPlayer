using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;

namespace LinkerPlayer.View.UserControls;

public partial class BottomControlPanel : INotifyPropertyChanged
{
    public bool Rendering;

    public BottomControlPanel()
    {
        DataContext = this;
        InitializeComponent();

        State = ButtonState.Paused;
        Mode = PlaybackMode.Loop;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public enum ButtonState
    {
        Stopped,
        Playing,
        Paused
    }

    public enum PlaybackMode
    {
        NoLoop,
        Loop1,
        Loop
    }

    private string _buttonStateImagePath = string.Empty;
    private string _playbackModeImagePath = string.Empty;
    private ButtonState _buttonState = ButtonState.Stopped;
    private PlaybackMode _playbackMode = PlaybackMode.NoLoop;

    public string ButtonStateImagePath
    {
        get => _buttonStateImagePath;
        set
        {
            _buttonStateImagePath = value;

            OnPropertyChanged();
        }
    }

    public string PlaybackModeImagePath
    {
        get => _playbackModeImagePath;
        set
        {
            _playbackModeImagePath = value;

            OnPropertyChanged();
        }
    }

    public ButtonState State
    {
        get => _buttonState;
        set
        {
            switch (value)
            {
                case ButtonState.Paused:
                    ButtonStateImagePath = "/Resources/Images/play.png";
                    _buttonState = value;
                    break;
                case ButtonState.Playing:
                    ButtonStateImagePath = "/Resources/Images/pause.png";
                    _buttonState = value;
                    break;
            }
        }
    }

    public PlaybackMode Mode
    {
        get => _playbackMode;
        set
        {
            switch (value)
            {
                case PlaybackMode.Loop:
                    PlaybackModeImagePath = "/Resources/Images/Loop.png";
                    _playbackMode = value;
                    break;
                case PlaybackMode.Loop1:
                    PlaybackModeImagePath = "/Resources/Images/Loop1.png";
                    _playbackMode = value;
                    break;
                case PlaybackMode.NoLoop:
                    PlaybackModeImagePath = "/Resources/Images/NoLoop.png";
                    _playbackMode = value;
                    break;
            }
        }
    }

    private void PlaybackModeButton_Click(object sender, RoutedEventArgs e)
    {
        switch (Mode)
        {
            case PlaybackMode.Loop:
                Mode = PlaybackMode.Loop1;
                break;
            case PlaybackMode.Loop1:
                Mode = PlaybackMode.NoLoop;
                break;
            case PlaybackMode.NoLoop:
                Mode = PlaybackMode.Loop;
                break;
        }
    }

    DispatcherTimer? _vsHeightTimer;
    private bool _isToggling;

    private void VSExpandTimer_Tick(object sender, EventArgs e)
    {
        GridLength rowHeight = VolumeSlidersGrid.RowDefinitions[0].Height;

        if (rowHeight.Value < 100)
        {
            VolumeSlidersGrid.RowDefinitions[0].Height = new GridLength(rowHeight.Value + 5, GridUnitType.Star);
            VolumeSlidersGrid.RowDefinitions[1].Height = new GridLength(rowHeight.Value + 5, GridUnitType.Star);
        }
        else
        {
            _isToggling = false;
            _vsHeightTimer?.Stop();
        }
    }

    private void VSContractTimer_Tick(object sender, EventArgs e)
    {
        GridLength rowHeight = VolumeSlidersGrid.RowDefinitions[0].Height;

        if (rowHeight.Value > 0)
        {
            VolumeSlidersGrid.RowDefinitions[0].Height = new GridLength(rowHeight.Value - 5, GridUnitType.Star);
            VolumeSlidersGrid.RowDefinitions[1].Height = new GridLength(rowHeight.Value - 5, GridUnitType.Star);
        }
        else
        {
            _isToggling = false;
            _vsHeightTimer?.Stop();
        }
    }

    private void ToggleVolumeSliders(object sender, RoutedEventArgs e)
    {
        if (!_isToggling)
        {
            _vsHeightTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(5)
            };

            if (VolumeSlidersGrid.RowDefinitions[0].Height.Value == 0)
            {
                _vsHeightTimer.Tick += VSExpandTimer_Tick!;

                RotateToggle(0, -180);
            }
            else
            {
                _vsHeightTimer.Tick += VSContractTimer_Tick!;

                RotateToggle(-180, 0);
            }

            _isToggling = true;
            _vsHeightTimer.Start();
        }
    }

    private void RotateToggle(double from, double to)
    {
        DoubleAnimation rotateAnimation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut }
        };

        ExpanderImage.RenderTransformOrigin = new Point(0.5, 0.5);

        RotateTransform rotateTransform = new RotateTransform();
        ExpanderImage.RenderTransform = rotateTransform;

        rotateTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnimation);
    }

    public void ShowSeekBarHideBorders()
    {
        // reset SeekBar scaling to 1
        SeekBar.RenderTransformOrigin = new Point(0.5, 0.5);
        SeekBar.RenderTransform = new ScaleTransform() { ScaleY = 1 };

        // animate SeekBar opacity from 0 to SeekBar.Opacity
        DoubleAnimation seekBarOpacityAnimation = new DoubleAnimation
        {
            From = SeekBar.Opacity,
            To = 1,
            Duration = TimeSpan.FromSeconds(1),
            EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut }
        };

        Storyboard storyboardSeekBarOpacity = new Storyboard();

        Storyboard.SetTarget(seekBarOpacityAnimation, SeekBar);
        Storyboard.SetTargetProperty(seekBarOpacityAnimation, new PropertyPath(OpacityProperty));
        storyboardSeekBarOpacity.Children.Add(seekBarOpacityAnimation);

        storyboardSeekBarOpacity.Begin();

        // animate UniGrid scaleY from 1 to 0
        UniGrid.RenderTransformOrigin = new Point(0.5, 0.5);
        UniGrid.RenderTransform = new ScaleTransform() { ScaleY = 1 };

        DoubleAnimation uniGridScaleAnimation = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromSeconds(0.3),
            EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut }
        };

        Storyboard storyboardUniGridScale = new Storyboard();

        Storyboard.SetTarget(uniGridScaleAnimation, UniGrid);
        Storyboard.SetTargetProperty(uniGridScaleAnimation,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
        storyboardUniGridScale.Children.Add(uniGridScaleAnimation);
        storyboardUniGridScale.Begin();
    }

    public void HideSeekBarShowBorders()
    {
        SeekBar.RenderTransformOrigin = new Point(0.5, 0.5);
        SeekBar.RenderTransform = new ScaleTransform() { ScaleY = 1 };

        // animate SeekBar opacity from 1 to 0
        DoubleAnimation seekBarOpacityAnimation = new DoubleAnimation
        {
            From = SeekBar.Opacity,
            To = 0,
            Duration = TimeSpan.FromSeconds(0.3),
            EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut }
        };

        Storyboard storyboardSeekBarOpacity = new Storyboard();

        Storyboard.SetTarget(seekBarOpacityAnimation, SeekBar);
        Storyboard.SetTargetProperty(seekBarOpacityAnimation, new PropertyPath(OpacityProperty));
        storyboardSeekBarOpacity.Children.Add(seekBarOpacityAnimation);
        storyboardSeekBarOpacity.Begin();

        // animate UniGrid scaleY from 0 to 1
        UniGrid.RenderTransformOrigin = new Point(0.5, 0.5);
        UniGrid.RenderTransform = new ScaleTransform() { ScaleY = 1 };
        DoubleAnimation uniGridScaleAnimation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromSeconds(0.3),
            EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut }
        };

        uniGridScaleAnimation.Completed += (_, _) =>
        {
            // set SeekBar scaling to 2
            SeekBar.RenderTransformOrigin = new Point(0.5, 0.5);
            SeekBar.RenderTransform = new ScaleTransform() { ScaleY = 2 };
        };

        Storyboard storyboardUniGridScale = new Storyboard();

        Storyboard.SetTarget(uniGridScaleAnimation, UniGrid);
        Storyboard.SetTargetProperty(uniGridScaleAnimation,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
        storyboardUniGridScale.Children.Add(uniGridScaleAnimation);
        storyboardUniGridScale.Begin();
    }

    public async void VisualizeAudio(string? path)
    {
        if (Rendering)
        {
            Rendering = false;
            await Task.Delay(10);
        }
        else
        {
            ShowSeekBarHideBorders();
        }

        List<float> peaks = new List<float>();

        await Render(path, peaks);

        if (peaks.Count == 0)
        {
            return;
        }

        UniGrid.Children.Clear();

        foreach (float peak in peaks)
        {
            UniGrid.Children.Add(new Border()
            {
                CornerRadius = new CornerRadius(2),
                Height = peak,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#673ab7")),
                Opacity = 0.4,
                Margin = new Thickness(1)
            });
        }

        UniGrid_SizeChanged(null, null);
        SeekBar_ValueChanged(null, null);

        HideSeekBarShowBorders();
    }

    private async Task Render(string? path, List<float> peaks)
    {
        await Task.Run(() =>
        {
            Rendering = true;

            using Mp3FileReader mp3 = new Mp3FileReader(path);
            int peakCount = 300;

            int bytesPerSample = (mp3.WaveFormat.BitsPerSample / 8) * mp3.WaveFormat.Channels;
            int samplesPerPeak = (int)(mp3.Length / (double)(peakCount * bytesPerSample));
            int bytesPerPeak = bytesPerSample * samplesPerPeak;

            byte[] buffer = new byte[bytesPerPeak];

            for (int x = 0; x < peakCount; x++)
            {
                if (!Rendering)
                {
                    peaks.Clear();

                    return;
                }

                int bytesRead = mp3.Read(buffer, 0, bytesPerPeak);
                if (bytesRead == 0)
                    break;

                float sum = 0;

                for (int n = 0; n < bytesRead; n += 2)
                {
                    if (!Rendering)
                    {
                        peaks.Clear();

                        return;
                    }

                    sum += Math.Abs(BitConverter.ToInt16(buffer, n));
                }

                // ReSharper disable once PossibleLossOfFraction
                float average = sum / (bytesRead / 2);

                peaks.Add(average);
            }

            if (peaks.Count != 0)
            {
                float peaksMax = peaks.Max();
                for (int i = 0; i < peaks.Count; i++)
                {
                    if (!Rendering)
                    {
                        peaks.Clear();

                        return;
                    }

                    peaks[i] = (peaks[i] / peaksMax) * (int)(UniGrid.ActualHeight * 0.95); // peak height

                    if (peaks[i] < 2)
                    {
                        peaks[i] = 2;
                    }
                }
            }

            Rendering = false;
        });
    }

    private void UniGrid_SizeChanged(object? sender, SizeChangedEventArgs? e)
    {
        int n = UniGrid.Children.Count;

        if (n == 0)
        {
            return;
        }

        int k = (int)UniGrid.ActualWidth / 6;

        List<int> numbers = new List<int>();
        for (int i = 0; i < n; i++)
        {
            numbers.Add(i);
        }

        List<int> reducedList = numbers.EvenlySpacedSubset(k);

        for (int i = 0; i < UniGrid.Children.Count; i++)
        {
            if (UniGrid.Children[i] is Border border)
            {
                border.Visibility = reducedList.Contains(i) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void SeekBar_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double>? e)
    {
        double val = SeekBar.Value;
        UIElementCollection borders = UniGrid.Children;

        int before = (int)(borders.Count * val / 100);

        for (int i = 0; i < borders.Count; i++)
        {
            if (i < before)
                ((borders[i] as Border)!).Opacity = 1;
            else
                ((borders[i] as Border)!).Opacity = 0.4;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = null!)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void MainVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        PackIcon? icon = MainVolumeButton.Content as PackIcon;
        double val = MainVolumeSlider.Value;

        if (val == 0)
        {
            if (icon != null) icon.Kind = PackIconKind.VolumeMute;
        }
        else if (val < 50)
        {
            if (icon != null) icon.Kind = PackIconKind.VolumeMedium;
        }
        else if (val >= 50)
        {
            if (icon != null) icon.Kind = PackIconKind.VolumeHigh;
        }
    }

    double _mainVolumeSliderBeforeMuteValue;

    private void MainVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (MainVolumeSlider.Value != 0)
        {
            _mainVolumeSliderBeforeMuteValue = MainVolumeSlider.Value;
            //MainVolumeSlider.Value = 0;

            AnimateVolumeSliderValue(MainVolumeSlider, 0);
        }
        else
        {
            //MainVolumeSlider.Value = mainVolumeSliderBeforeMuteValue;

            AnimateVolumeSliderValue(MainVolumeSlider, _mainVolumeSliderBeforeMuteValue);
        }
    }

    private void MicVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        PackIcon? icon = MicVolumeButton.Content as PackIcon;

        if (MicVolumeSlider.Value == 0)
        {
            if (icon != null) icon.Kind = PackIconKind.MicrophoneOff;
        }
        else
        {
            if (icon != null) icon.Kind = PackIconKind.Microphone;
        }
    }

    double _micVolumeSliderBeforeMuteValue;

    private void MicVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (MicVolumeSlider.Value != 0)
        {
            _micVolumeSliderBeforeMuteValue = MicVolumeSlider.Value;
            //MicVolumeSlider.Value = 0;

            AnimateVolumeSliderValue(MicVolumeSlider, 0);
        }
        else
        {
            //MicVolumeSlider.Value = micVolumeSliderBeforeMuteValue;

            AnimateVolumeSliderValue(MicVolumeSlider, _micVolumeSliderBeforeMuteValue);
        }
    }

    private void AdditionalVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        PackIcon? icon = AdditionalVolumeButton.Content as PackIcon;

        if (AdditionalVolumeSlider.Value == 0)
        {
            if (icon != null) icon.Kind = PackIconKind.MicrophoneVariantOff;
        }
        else
        {
            if (icon != null) icon.Kind = PackIconKind.MicrophoneVariant;
        }
    }

    double _additionalVolumeSliderBeforeMuteValue;

    private void AdditionalVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (AdditionalVolumeSlider.Value != 0)
        {
            _additionalVolumeSliderBeforeMuteValue = AdditionalVolumeSlider.Value;

            AnimateVolumeSliderValue(AdditionalVolumeSlider, 0);
        }
        else
        {
            AnimateVolumeSliderValue(AdditionalVolumeSlider, _additionalVolumeSliderBeforeMuteValue);
        }
    }

    private void AnimateVolumeSliderValue(Slider slider, double newVal)
    {
        DoubleAnimation doubleAnimation = new DoubleAnimation
        {
            From = slider.Value,
            To = newVal,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut }
        };

        slider.BeginAnimation(RangeBase.ValueProperty, doubleAnimation);
    }
}

public static class ListExtensions
{
    public static List<T> EvenlySpacedSubset<T>(this List<T> list, int count)
    {
        int length = list.Count;
        int[] indices = Enumerable.Range(0, count)
            .Select(i => (int)Math.Round((double)(i * (length - 1)) / (count - 1)))
            .ToArray();
        return indices.Select(i => list[i]).ToList();
    }
}