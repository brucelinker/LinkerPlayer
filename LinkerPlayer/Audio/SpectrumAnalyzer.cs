using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LinkerPlayer.Audio;

/// <summary>
/// A spectrum analyzer control for visualizing audio level and frequency data.
/// </summary>
[DisplayName("Spectrum Analyzer")]
[Description("Displays audio level and frequency data.")]
[ToolboxItem(true)]
[TemplatePart(Name = "PART_SpectrumCanvas", Type = typeof(Canvas))]
public partial class SpectrumAnalyzer : Control
{
    #region Enums
    public enum BarHeightScalingStyles
    {
        Decibel, // 20 * Log10(FFTValue), scaled -90 to 0 dB
        Sqrt,    // Sqrt(FFTValue) * 2 * BarHeight
        Linear   // 9 * FFTValue * BarHeight
    }

    public enum FftDataSize
    {
        Fft256 = 256,
        Fft512 = 512,
        Fft1024 = 1024,
        Fft2048 = 2048,
        Fft4096 = 4096,
        Fft8192 = 8192,
        Fft16384 = 16384
    }

    public enum ShapeType
    {
        Rectangle,
        Ellipse,
        Triangle
    }
    #endregion

    #region Fields
    private readonly DispatcherTimer _animationTimer;
    private Canvas? _spectrumCanvas;
    private ISpectrumPlayer? _soundPlayer;
    private readonly List<Shape> _barShapes = [];
    private readonly List<Shape> _peakShapes = [];
    private float[] _channelData = new float[2048];
    private float[] _channelPeakData = [];
    private readonly ILogger<SpectrumAnalyzer> _logger;
    private int[] _barIndexMax = [];
    private int[] _barLogScaleIndexMax = [];
    #endregion

    #region Constants
    private const int ScaleFactorLinear = 9;
    private const int ScaleFactorSqr = 2;
    private const double MinDbValue = -90;
    private const double MaxDbValue = 0;
    private const double DbScale = MaxDbValue - MinDbValue;
    private const int DefaultUpdateInterval = 25;
    #endregion

    #region Dependency Properties
    public static readonly DependencyProperty MaximumFrequencyProperty =
        DependencyProperty.Register(nameof(MaximumFrequency), typeof(int), typeof(SpectrumAnalyzer),
            new UIPropertyMetadata(20000, OnPropertyChanged, OnCoerceMaximumFrequency));

    public int MaximumFrequency
    {
        get => (int)GetValue(MaximumFrequencyProperty);
        set => SetValue(MaximumFrequencyProperty, value);
    }

    public static readonly DependencyProperty MinimumFrequencyProperty =
        DependencyProperty.Register(nameof(MinimumFrequency), typeof(int), typeof(SpectrumAnalyzer),
            new UIPropertyMetadata(20, OnPropertyChanged, OnCoerceMinimumFrequency));

    public int MinimumFrequency
    {
        get => (int)GetValue(MinimumFrequencyProperty);
        set => SetValue(MinimumFrequencyProperty, value);
    }

    public static readonly DependencyProperty BarCountProperty =
        DependencyProperty.Register(nameof(BarCount), typeof(int), typeof(SpectrumAnalyzer),
            new UIPropertyMetadata(32, OnPropertyChanged, OnCoerceBarCount));

    public int BarCount
    {
        get => (int)GetValue(BarCountProperty);
        set => SetValue(BarCountProperty, value);
    }

    public static readonly DependencyProperty BarSpacingProperty =
        DependencyProperty.Register(nameof(BarSpacing), typeof(double), typeof(SpectrumAnalyzer),
            new UIPropertyMetadata(5.0d, OnPropertyChanged, OnCoerceBarSpacing));

    public double BarSpacing
    {
        get => (double)GetValue(BarSpacingProperty);
        set => SetValue(BarSpacingProperty, value);
    }

    public static readonly DependencyProperty PeakFallDelayProperty =
        DependencyProperty.Register(nameof(PeakFallDelay), typeof(int), typeof(SpectrumAnalyzer),
            new UIPropertyMetadata(10, OnPropertyChanged, OnCoercePeakFallDelay));

    public int PeakFallDelay
    {
        get => (int)GetValue(PeakFallDelayProperty);
        set => SetValue(PeakFallDelayProperty, value);
    }

    public static readonly DependencyProperty BarDecaySpeedProperty =
        DependencyProperty.Register(nameof(BarDecaySpeed), typeof(double), typeof(SpectrumAnalyzer),
            new UIPropertyMetadata(1.0, OnPropertyChanged, OnCoerceBarDecaySpeed));

    public double BarDecaySpeed
    {
        get => (double)GetValue(BarDecaySpeedProperty);
        set => SetValue(BarDecaySpeedProperty, value);
    }

    private static object OnCoerceBarDecaySpeed(DependencyObject d, object value)
    {
        return Math.Max((double)value, 0.1);
    }

    public static readonly DependencyProperty IsFrequencyScaleLinearProperty =
        DependencyProperty.Register(nameof(IsFrequencyScaleLinear), typeof(bool), typeof(SpectrumAnalyzer),
            new UIPropertyMetadata(false, OnPropertyChanged));

    public bool IsFrequencyScaleLinear
    {
        get => (bool)GetValue(IsFrequencyScaleLinearProperty);
        set => SetValue(IsFrequencyScaleLinearProperty, value);
    }

    public static readonly DependencyProperty BarHeightScalingProperty =
        DependencyProperty.Register(nameof(BarHeightScaling), typeof(BarHeightScalingStyles), typeof(SpectrumAnalyzer),
            new UIPropertyMetadata(BarHeightScalingStyles.Decibel, OnPropertyChanged));

    public BarHeightScalingStyles BarHeightScaling
    {
        get => (BarHeightScalingStyles)GetValue(BarHeightScalingProperty);
        set => SetValue(BarHeightScalingProperty, value);
    }

    public static readonly DependencyProperty AveragePeaksProperty =
        DependencyProperty.Register(nameof(AveragePeaks), typeof(bool), typeof(SpectrumAnalyzer),
            new UIPropertyMetadata(false, OnPropertyChanged));

    public bool AveragePeaks
    {
        get => (bool)GetValue(AveragePeaksProperty);
        set => SetValue(AveragePeaksProperty, value);
    }

    public static readonly DependencyProperty BarStyleProperty =
        DependencyProperty.Register(nameof(BarStyle), typeof(Style), typeof(SpectrumAnalyzer),
            new UIPropertyMetadata(null, OnPropertyChanged));

    public Style BarStyle
    {
        get => (Style)GetValue(BarStyleProperty);
        set => SetValue(BarStyleProperty, value);
    }

    public static readonly DependencyProperty PeakStyleProperty =
        DependencyProperty.Register(nameof(PeakStyle), typeof(Style), typeof(SpectrumAnalyzer),
            new UIPropertyMetadata(null, OnPropertyChanged));

    public Style PeakStyle
    {
        get => (Style)GetValue(PeakStyleProperty);
        set => SetValue(PeakStyleProperty, value);
    }

    public static readonly DependencyProperty ActualBarWidthProperty =
        DependencyProperty.Register(nameof(ActualBarWidth), typeof(double), typeof(SpectrumAnalyzer),
            new UIPropertyMetadata(0.0d));

    public double ActualBarWidth
    {
        get => (double)GetValue(ActualBarWidthProperty);
        set => SetValue(ActualBarWidthProperty, value);
    }

    public static readonly DependencyProperty RefreshIntervalProperty =
        DependencyProperty.Register(nameof(RefreshInterval), typeof(int), typeof(SpectrumAnalyzer),
            new UIPropertyMetadata(DefaultUpdateInterval, OnRefreshIntervalChanged, OnCoerceRefreshInterval));

    public int RefreshInterval
    {
        get => (int)GetValue(RefreshIntervalProperty);
        set => SetValue(RefreshIntervalProperty, value);
    }

    public static readonly DependencyProperty FftComplexityProperty =
        DependencyProperty.Register(nameof(FftComplexity), typeof(FftDataSize), typeof(SpectrumAnalyzer),
            new UIPropertyMetadata(FftDataSize.Fft2048, OnFftComplexityChanged, OnCoerceFftComplexity));

    public FftDataSize FftComplexity
    {
        get => (FftDataSize)GetValue(FftComplexityProperty);
        set => SetValue(FftComplexityProperty, value);
    }

    public static readonly DependencyProperty BarShapeTypeProperty =
        DependencyProperty.Register(nameof(BarShapeType), typeof(ShapeType), typeof(SpectrumAnalyzer),
            new UIPropertyMetadata(ShapeType.Rectangle, OnPropertyChanged));

    public ShapeType BarShapeType
    {
        get => (ShapeType)GetValue(BarShapeTypeProperty);
        set => SetValue(BarShapeTypeProperty, value);
    }

    public static readonly DependencyProperty PeakShapeTypeProperty =
        DependencyProperty.Register(nameof(PeakShapeType), typeof(ShapeType), typeof(SpectrumAnalyzer),
            new UIPropertyMetadata(ShapeType.Rectangle, OnPropertyChanged));

    public ShapeType PeakShapeType
    {
        get => (ShapeType)GetValue(PeakShapeTypeProperty);
        set => SetValue(PeakShapeTypeProperty, value);
    }

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SpectrumAnalyzer sa)
            sa.UpdateBarLayout();
    }

    private static void OnRefreshIntervalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SpectrumAnalyzer sa)
            sa._animationTimer.Interval = TimeSpan.FromMilliseconds((int)e.NewValue);
    }

    private static void OnFftComplexityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SpectrumAnalyzer sa)
            sa._channelData = new float[(int)(FftDataSize)e.NewValue];
    }

    private static object OnCoerceMaximumFrequency(DependencyObject d, object value)
    {
        if (d is SpectrumAnalyzer sa)
            return Math.Max((int)value, sa.MinimumFrequency + 1);
        return value;
    }

    private static object OnCoerceMinimumFrequency(DependencyObject d, object value)
    {
        return Math.Max((int)value, 0);
    }

    private static object OnCoerceBarCount(DependencyObject d, object value)
    {
        return Math.Max((int)value, 1);
    }

    private static object OnCoerceBarSpacing(DependencyObject d, object value)
    {
        return Math.Max((double)value, 0);
    }

    private static object OnCoercePeakFallDelay(DependencyObject d, object value)
    {
        return Math.Max((int)value, 0);
    }

    private static object OnCoerceRefreshInterval(DependencyObject d, object value)
    {
        return Math.Min(1000, Math.Max(10, (int)value));
    }

    private static object OnCoerceFftComplexity(DependencyObject d, object value)
    {
        return value;
    }
    #endregion

    #region Constructor
    public SpectrumAnalyzer()
    {
        _logger = App.AppHost.Services.GetRequiredService<ILogger<SpectrumAnalyzer>>();
        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(DefaultUpdateInterval)
        };
        _animationTimer.Tick += AnimationTimer_Tick;
    }
    #endregion

    #region Public Methods
    public void RegisterSoundPlayer(ISpectrumPlayer soundPlayer)
    {
        if (_soundPlayer != null)
        {
            _soundPlayer.PropertyChanged -= SoundPlayer_PropertyChanged;
            _soundPlayer.OnFftCalculated -= SoundPlayer_OnFftCalculated;
        }

        _soundPlayer = soundPlayer;
        if (_soundPlayer != null)
        {
            _soundPlayer.PropertyChanged += SoundPlayer_PropertyChanged;
            _soundPlayer.OnFftCalculated += SoundPlayer_OnFftCalculated;

            UpdateBarLayout();
            if (_soundPlayer.IsPlaying)
                _animationTimer.Start();
            _logger.LogInformation("SpectrumAnalyzer: Registered sound player");
        }
    }

    public void UnregisterSoundPlayer()
    {
        if (_soundPlayer != null)
        {
            _soundPlayer.PropertyChanged -= SoundPlayer_PropertyChanged;
            _soundPlayer.OnFftCalculated -= SoundPlayer_OnFftCalculated;
            _soundPlayer = null;

            if (_spectrumCanvas != null)
                UpdateSpectrumShapes();
            if (!_animationTimer.IsEnabled)
                _animationTimer.Start();
            _logger.LogInformation("SpectrumAnalyzer: Sound player unregistered");
        }
    }
    #endregion

    #region Template Overrides
    public override void OnApplyTemplate()
    {
        _spectrumCanvas = GetTemplateChild("PART_SpectrumCanvas") as Canvas;
        if (_spectrumCanvas != null)
            _spectrumCanvas.SizeChanged += SpectrumCanvas_SizeChanged;
        UpdateBarLayout();
    }

    protected override void OnTemplateChanged(ControlTemplate oldTemplate, ControlTemplate newTemplate)
    {
        base.OnTemplateChanged(oldTemplate, newTemplate);
        if (_spectrumCanvas != null)
        {
            _spectrumCanvas.SizeChanged -= SpectrumCanvas_SizeChanged;
            _spectrumCanvas = null;
        }
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (oldParent != null && VisualParent == null)
        {
            _logger.LogInformation("SpectrumAnalyzer: OnVisualParentChanged called, stopping timer, oldParent={OldParent}, VisualParent={VisualParent}",
                oldParent?.GetType().Name ?? "null", VisualParent?.GetType().Name ?? "null");
            UnregisterSoundPlayer();
        }
    }
    #endregion

    #region Event Overrides
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        UpdateBarLayout();
        if (_soundPlayer != null && _soundPlayer.IsPlaying)
            UpdateSpectrum();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateBarLayout();
        if (_soundPlayer != null && _soundPlayer.IsPlaying)
            UpdateSpectrum();
    }
    #endregion

    #region Shape Creation
    private Shape CreateShape(ShapeType type, double width, double height, Style? style, double x, double y)
    {
        Shape shape = type switch
        {
            ShapeType.Rectangle => new Rectangle { Width = width, Height = height },
            ShapeType.Ellipse => new Ellipse { Width = width, Height = height },
            ShapeType.Triangle => new Polygon
            {
                Points = new PointCollection(new[] { new Point(0, height), new Point(width / 2, 0), new Point(width, height) }),
                Width = width,
                Height = height
            },
            _ => new Rectangle { Width = width, Height = height }
        };
        shape.Style = style;
        shape.Margin = new Thickness(x, y, 0, 0);
        return shape;
    }
    #endregion

    #region Private Drawing Methods
    private void UpdateSpectrum()
    {
        if (_spectrumCanvas == null || _spectrumCanvas.RenderSize.Width < 1 || _spectrumCanvas.RenderSize.Height < 1)
        {
            _logger.LogDebug("SpectrumAnalyzer: UpdateSpectrum skipped - canvas is null or invalid size");
            return;
        }

        if (_soundPlayer == null && _channelPeakData.All(p => p < 0.05f))
        {
            _animationTimer.Stop();
            _logger.LogDebug("SpectrumAnalyzer: Timer stopped in UpdateSpectrum - no sound player and peaks decayed");
            return;
        }

        if (_soundPlayer != null && !_soundPlayer.IsPlaying)
        {
            UpdateSpectrumShapes();
            return;
        }

        if (_soundPlayer != null && !_soundPlayer.GetFftData(_channelData))
        {
            _logger.LogDebug("SpectrumAnalyzer: Skipped update - no FFT data");
            UpdateSpectrumShapes(); // Continue decay even if no FFT data
            return;
        }

        UpdateSpectrumShapes();
    }

    public event EventHandler<float[]>? BarValuesChanged;
    private void UpdateSpectrumShapes()
    {
        if (_spectrumCanvas == null)
            return;

        bool allZero = true;
        double fftBucketHeight = 0;
        double barHeight = 0;
        double lastPeakHeight = 0;
        double height = _spectrumCanvas.RenderSize.Height;
        int barIndex = 0;
        double peakDotHeight = Math.Max(ActualBarWidth / 2.0, 1);
        double barHeightScale = height - peakDotHeight;

        int maxFreqIndex = _soundPlayer?.GetFftFrequencyIndex(MaximumFrequency) + 1 ?? 2047;
        int minFreqIndex = _soundPlayer?.GetFftFrequencyIndex(MinimumFrequency) ?? 0;
        maxFreqIndex = Math.Min(maxFreqIndex, 2047);

        double[] barHeights = new double[_barShapes.Count]; // Track current bar heights for decay

        for (int i = minFreqIndex; i <= maxFreqIndex; i++)
        {
            if (_soundPlayer == null || !_soundPlayer.IsPlaying)
            {
                // Decay bars gradually
                barHeight = barHeights[barIndex] = (float)(barHeights[barIndex] + BarDecaySpeed * barHeights[barIndex]) / (BarDecaySpeed + 1);
            }
            else
            {
                switch (BarHeightScaling)
                {
                    case BarHeightScalingStyles.Decibel:
                        double dbValue = 20 * Math.Log10(_channelData[i] > 0 ? _channelData[i] : 1e-5);
                        fftBucketHeight = (dbValue - MinDbValue) / DbScale * barHeightScale;
                        break;
                    case BarHeightScalingStyles.Linear:
                        fftBucketHeight = _channelData[i] * ScaleFactorLinear * barHeightScale;
                        break;
                    case BarHeightScalingStyles.Sqrt:
                        fftBucketHeight = Math.Sqrt(_channelData[i]) * ScaleFactorSqr * barHeightScale;
                        break;
                }
                barHeight = Math.Max(barHeight, fftBucketHeight);
                barHeight = Math.Max(barHeight, 0);
                barHeights[barIndex] = barHeight;
            }

            int currentIndexMax = IsFrequencyScaleLinear ? _barIndexMax[barIndex] : _barLogScaleIndexMax[barIndex];
            if (i == currentIndexMax)
            {
                barHeight = Math.Min(barHeight, height);
                if (AveragePeaks && barIndex > 0)
                    barHeight = (lastPeakHeight + barHeight) / 2;

                double peakYPos = barHeight;
                _channelPeakData[barIndex] = (float)Math.Max(_channelPeakData[barIndex], peakYPos);
                _channelPeakData[barIndex] = (float)(peakYPos + PeakFallDelay * _channelPeakData[barIndex]) / (PeakFallDelay + 1);

                double xCoord = BarSpacing + ActualBarWidth * barIndex + BarSpacing * barIndex + 1;
                _barShapes[barIndex].Margin = new Thickness(xCoord, height - barHeight, 0, 0);
                _barShapes[barIndex].Height = barHeight;
                _peakShapes[barIndex].Margin = new Thickness(xCoord, height - _channelPeakData[barIndex] - peakDotHeight, 0, 0);
                _peakShapes[barIndex].Height = peakDotHeight;

                if (_channelPeakData[barIndex] > 0.05)
                    allZero = false;

                lastPeakHeight = barHeight;
                barHeight = 0;
                barIndex++;
            }
        }

        if (allZero && (_soundPlayer == null || !_soundPlayer.IsPlaying))
        {
            _animationTimer.Stop();
            _logger.LogDebug("SpectrumAnalyzer: Timer stopped in UpdateSpectrumShapes - all peaks zero");
        }

        BarValuesChanged?.Invoke(this, _channelPeakData);
    }

    private void UpdateBarLayout()
    {
        if (_spectrumCanvas == null)
            return;

        double barWidth = Math.Max((_spectrumCanvas.RenderSize.Width - BarSpacing * (BarCount + 1)) / BarCount, 1);
        int actualBarCount = barWidth >= 1.0 ? BarCount : Math.Max((int)((_spectrumCanvas.RenderSize.Width - BarSpacing) / (barWidth + BarSpacing)), 1);
        _channelPeakData = new float[actualBarCount];

        int maxFreqIndex = _soundPlayer?.GetFftFrequencyIndex(MaximumFrequency) + 1 ?? 2047;
        int minFreqIndex = _soundPlayer?.GetFftFrequencyIndex(MinimumFrequency) ?? 0;
        maxFreqIndex = Math.Min(maxFreqIndex, 2047);

        int indexCount = maxFreqIndex - minFreqIndex;
        int linearIndexBucketSize = (int)Math.Round(indexCount / (double)actualBarCount, 0);
        List<int> maxIndexList = [];
        List<int> maxLogScaleIndexList = [];
        double maxLog = Math.Log(actualBarCount, actualBarCount);

        for (int i = 1; i < actualBarCount; i++)
        {
            maxIndexList.Add(minFreqIndex + i * linearIndexBucketSize);
            int logIndex = (int)((maxLog - Math.Log(actualBarCount + 1 - i, actualBarCount + 1)) * indexCount) + minFreqIndex;
            maxLogScaleIndexList.Add(logIndex);
        }

        maxIndexList.Add(maxFreqIndex);
        maxLogScaleIndexList.Add(maxFreqIndex);
        _barIndexMax = maxIndexList.ToArray();
        _barLogScaleIndexMax = maxLogScaleIndexList.ToArray();

        _spectrumCanvas.Children.Clear();
        _barShapes.Clear();
        _peakShapes.Clear();

        double height = _spectrumCanvas.RenderSize.Height;
        double peakDotHeight = Math.Max(barWidth / 2.0, 1);

        for (int i = 0; i < actualBarCount; i++)
        {
            double xCoord = BarSpacing + barWidth * i + BarSpacing * i + 1;
            var barShape = CreateShape(BarShapeType, barWidth, 0, BarStyle, xCoord, height);
            var peakShape = CreateShape(PeakShapeType, barWidth, peakDotHeight, PeakStyle, xCoord, height - peakDotHeight);
            _barShapes.Add(barShape);
            _peakShapes.Add(peakShape);
            _spectrumCanvas.Children.Add(barShape);
            _spectrumCanvas.Children.Add(peakShape);
        }

        ActualBarWidth = barWidth;
    }
    #endregion

    #region Event Handlers
    private void SoundPlayer_OnFftCalculated(float[] fftData)
    {
        if (fftData.Length == 1024 && _channelData.Length == 2048)
        {
            Array.Copy(fftData, 0, _channelData, 0, fftData.Length);
            Array.Clear(_channelData, fftData.Length, _channelData.Length - fftData.Length);
        }
        else
        {
            _logger.LogWarning("SpectrumAnalyzer: FFT data length mismatch, expected {Expected}, got {Actual}", _channelData.Length, fftData.Length);
        }
    }

    private void SoundPlayer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "IsPlaying" && _soundPlayer != null)
        {
            if (_soundPlayer.IsPlaying && !_animationTimer.IsEnabled)
            {
                _animationTimer.Start();
                _logger.LogDebug("SpectrumAnalyzer: Timer started due to IsPlaying=true");
            }
            else if (!_soundPlayer.IsPlaying)
            {
                UpdateSpectrumShapes();
                _logger.LogDebug("SpectrumAnalyzer: Update triggered due to IsPlaying=false");
            }
        }
    }

    private async void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (_soundPlayer == null && _channelPeakData.All(p => p < 0.05f))
        {
            _animationTimer.Stop();
            _logger.LogDebug("SpectrumAnalyzer: Timer stopped in AnimationTimer_Tick - peaks decayed");
            return;
        }

        if (_soundPlayer != null && _soundPlayer.IsPlaying && _soundPlayer.GetFftData(_channelData))
        {
            Array.Copy(_channelData, _channelData, _channelData.Length);
        }

        UpdateSpectrum();
    }

    private void SpectrumCanvas_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateBarLayout();
    }
    #endregion
}