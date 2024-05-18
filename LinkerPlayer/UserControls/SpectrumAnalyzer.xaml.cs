using LinkerPlayer.ViewModels;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LinkerPlayer.UserControls;

/// <summary>
/// Interaction logic for SpectrumAnalyzer.xaml
/// </summary>
public partial class SpectrumAnalyzer
{
    private FrequencyBar[] _frequencyBars = Array.Empty<FrequencyBar>();
    private readonly SpectrumViewModel _spectrumViewModel = new();

    public SpectrumAnalyzer()
    {
        InitializeComponent();
        DataContext = _spectrumViewModel;
        Prepare();
    }

    public static readonly DependencyProperty MinimumDbLevelProperty =
        DependencyProperty.Register(nameof(MinimumDbLevel), typeof(double), typeof(SpectrumAnalyzer),
            new PropertyMetadata(-60d, MinimumDbLevelUpdated));

    public double MinimumDbLevel
    {
        get => (double)GetValue(MinimumDbLevelProperty);
        set => SetValue(MinimumDbLevelProperty, value);
    }

    private static void MinimumDbLevelUpdated(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        (d as SpectrumAnalyzer)?.UpdateDbDisplay((double)e.NewValue);
    }

    private void UpdateDbDisplay(double value)
    {
        Debug.WriteLine($"Value of dB: {value}");
        LowestDbLabel.Content = value.ToString(CultureInfo.InvariantCulture) + "dB";
    }

    public static readonly DependencyProperty MagnitudesProperty =
        DependencyProperty.Register(nameof(Magnitudes), typeof(double[]), typeof(SpectrumAnalyzer),
            new PropertyMetadata(MagnitudesUpdated));

    public double[] Magnitudes
    {
        get => (double[])GetValue(MagnitudesProperty);
        set => SetValue(MagnitudesProperty, value);
    }

    private static void MagnitudesUpdated(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        (d as SpectrumAnalyzer)?.UpdateMagnitudes((double[])e.NewValue);
    }

    private void UpdateMagnitudes(double[] mags)
    {
        for (int i = 0; i < mags.Length; i++)
        {
            double intensityDb = mags[i];

            if (intensityDb < MinimumDbLevel) intensityDb = MinimumDbLevel;

            // percent with -60 = 1
            double percent = intensityDb / MinimumDbLevel;

            // invert the percent using height of the bar element
            double barHeight = Spec0.ActualHeight - (percent * Spec0.ActualHeight);
            //var barHeight = _maximumFreqBarHeight - (percent * _maximumFreqBarHeight);

            // set height of control
            _frequencyBars[i].Height = barHeight > 2 ? barHeight : 2;

            //Debug.WriteLine($"Intensity: {intensityDB}, Percent: {percent}");
        }
    }

    private void Prepare()
    {
        _frequencyBars = new[]
        {
            new FrequencyBar(Spec1, Peak1),
            new FrequencyBar(Spec2, Peak2),
            new FrequencyBar(Spec3, Peak3),
            new FrequencyBar(Spec4, Peak4),
            new FrequencyBar(Spec5, Peak5),
            new FrequencyBar(Spec6, Peak6),
            new FrequencyBar(Spec7, Peak7),
            new FrequencyBar(Spec8, Peak8),
            new FrequencyBar(Spec9, Peak9),
            new FrequencyBar(Spec10, Peak10),
            new FrequencyBar(Spec11, Peak11),
            new FrequencyBar(Spec12, Peak12),
            new FrequencyBar(Spec13, Peak13),
            new FrequencyBar(Spec14, Peak14),
            new FrequencyBar(Spec15, Peak15),
            new FrequencyBar(Spec16, Peak16),
            new FrequencyBar(Spec17, Peak17),
            new FrequencyBar(Spec18, Peak18),
            new FrequencyBar(Spec19, Peak19)
        };
    }


    private class FrequencyBar
    {
        private bool _peakFalling;
        private double _lastPeakPosition;
        private readonly DispatcherTimer _peakTimer;
        private readonly DispatcherTimer _fallTimer;

        public FrequencyBar(Rectangle bar, Rectangle peak)
        {
            Bar = bar;
            Peak = peak;
            Bar.Height = 2;
            Peak.Height = 2;

            _peakTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            _peakTimer.Tick += Peak_Tick;

            _fallTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _fallTimer.Tick += Fall_Tick;
        }

        private void Fall_Tick(object? sender, EventArgs e)
        {
            if (Peak.Margin.Bottom > _lastPeakPosition - 20)
            {
                var margin = Peak.Margin;
                margin.Bottom -= 2;
                Peak.Margin = margin;
                Peak.Opacity -= .2;
            }
            else
            {
                _fallTimer.Stop();
                _peakFalling = false;
                var margin = Peak.Margin;
                margin.Bottom = 0;
                Peak.Margin = margin;
            }
        }

        private void Peak_Tick(object? sender, EventArgs e)
        {
            _peakTimer.Stop();
            if (!_peakFalling)
            {
                _peakFalling = true;
                _lastPeakPosition = Peak.Margin.Bottom;
                _fallTimer.Start();
            }
        }

        public double Height
        {
            set
            {
                Bar.Height = value;
                if (Bar.Height - 2 >= Peak.Margin.Bottom)
                {
                    _peakTimer.Stop();
                    _fallTimer.Stop();
                    _peakFalling = false;

                    var thickness = Peak.Margin;
                    thickness.Bottom = Bar.Height - 2;
                    Peak.Margin = thickness;
                    Peak.Opacity = 1;

                    _peakTimer.Start();
                }
            }
        }

        private Rectangle Bar { get; }
        private Rectangle Peak { get; }
    }
}