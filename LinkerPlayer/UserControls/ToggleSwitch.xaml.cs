using LinkerPlayer.Core;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LinkerPlayer.UserControls;

public partial class ToggleSwitch
{
    private static readonly ISettingsManager SettingsManager = App.AppHost.Services.GetRequiredService<ISettingsManager>();

    public ToggleSwitch()
    {
        InitializeComponent();
        Switched = (_, _) => { };
    }

    public EventHandler Switched;

    public static readonly DependencyProperty TrackBackgroundOnColorProperty = DependencyProperty.Register(
        nameof(TrackBackgroundOnColor), typeof(Color), typeof(ToggleSwitch), new PropertyMetadata(Colors.LightGray));
    public Color TrackBackgroundOnColor
    {
        get => (Color)GetValue(TrackBackgroundOnColorProperty);
        set
        {
            SetValue(TrackBackgroundOnColorProperty, value);
            if (IsOn)
            {
                BorderTrack.Background = new SolidColorBrush(value);
            }
        }
    }

    public static readonly DependencyProperty TrackBackgroundOffColorProperty = DependencyProperty.Register(
        nameof(TrackBackgroundOffColor), typeof(Color), typeof(ToggleSwitch), new PropertyMetadata(Colors.DarkGray));

    public Color TrackBackgroundOffColor
    {
        get => (Color)GetValue(TrackBackgroundOffColorProperty);
        set
        {
            SetValue(TrackBackgroundOffColorProperty, value);
            if (!IsOn)
            {
                BorderTrack.Background = new SolidColorBrush(value);
            }
        }
    }

    public static readonly DependencyProperty CircleBackgroundColorProperty = DependencyProperty.Register(
        nameof(CircleBackgroundColor), typeof(Color), typeof(ToggleSwitch), new PropertyMetadata(Colors.LightGray));

    public Color CircleBackgroundColor
    {
        get => (Color)GetValue(CircleBackgroundColorProperty);
        set
        {
            SetValue(CircleBackgroundColorProperty, value);
            EllipseToggle.Fill = new SolidColorBrush(value);
        }
    }

    public static readonly DependencyProperty CircleBorderColorProperty = DependencyProperty.Register(
        nameof(CircleBorderColor), typeof(Color), typeof(ToggleSwitch), new PropertyMetadata(Colors.SteelBlue));

    public Color CircleBorderColor
    {
        get => (Color)GetValue(CircleBorderColorProperty);
        set
        {
            SetValue(CircleBorderColorProperty, value);
            EllipseToggle.Stroke = new SolidColorBrush(value);
        }
    }

    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn), typeof(bool), typeof(ToggleSwitch), new PropertyMetadata(SettingsManager.Settings.EqualizerEnabled));

    public bool IsOn
    {
        get
        {
            //bool value = (bool)GetValue(IsOnProperty);
            if (ButtonToggle.Tag.ToString() == "On")
            {
                return true;
            }

            return false;
        }
        set
        {
            if (value == IsOn)
            {
                return;
            }

            SetValue(IsOnProperty, value);
            if (value)
            {
                ButtonToggle.Tag = "On";
                BorderTrack.Background = new SolidColorBrush(TrackBackgroundOffColor);
                ColorAnimation ca = new ColorAnimation(TrackBackgroundOnColor, TimeSpan.FromSeconds(.25));
                BorderTrack.Background.BeginAnimation(SolidColorBrush.ColorProperty, ca);
                DoubleAnimation da = new DoubleAnimation(10, TimeSpan.FromSeconds(.25));
                ToggleLabel.Content = "Equalizer is enabled";
                TranslateTransform.BeginAnimation(TranslateTransform.XProperty, da);
            }
            else
            {
                ButtonToggle.Tag = "Off";
                BorderTrack.Background = new SolidColorBrush(TrackBackgroundOnColor);
                ColorAnimation ca = new ColorAnimation(TrackBackgroundOffColor, TimeSpan.FromSeconds(.25));
                BorderTrack.Background.BeginAnimation(SolidColorBrush.ColorProperty, ca);
                DoubleAnimation da = new DoubleAnimation(-10, TimeSpan.FromSeconds(.25));
                ToggleLabel.Content = "Equalizer is disabled";
                TranslateTransform.BeginAnimation(TranslateTransform.XProperty, da);
            }
            Switched(this, EventArgs.Empty);
        }
    }

    private void buttonToggle_Click(object sender, RoutedEventArgs e)
    {
        IsOn = !IsOn;
    }
}
