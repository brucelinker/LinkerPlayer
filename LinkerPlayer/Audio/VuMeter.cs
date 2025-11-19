using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LinkerPlayer.Audio;

// Optional interface for future multi-channel engine support
public interface IChannelLevelProvider
{
    bool TryGetChannelDecibelLevels(out double[] levels); // levels length = channel count, dB values
}

[TemplatePart(Name = "PART_VuCanvas", Type = typeof(Canvas))]
public partial class VuMeter : Control
{
    #region Constants
    private const double MinDbValue = -60;
    private const double MaxDbValue = 10;
    private const double DbRange = MaxDbValue - MinDbValue;
    private const int DefaultUpdateInterval = 25;
    #endregion

    #region Fields
    private readonly System.Threading.Timer _animationTimer;
    private Canvas? _vuCanvas;
    private ISpectrumPlayer? _soundPlayer;
    private readonly ILogger<VuMeter> _logger;
    private readonly object _lockObject = new object();

    // Multi-channel dynamic collections
    private int _channelCount = 2;
    private double[] _channelLevels = System.Array.Empty<double>();
    private readonly System.Collections.Generic.List<Rectangle> _channelBars = new System.Collections.Generic.List<Rectangle>();

    // Cached property values
    private double _cachedDecaySpeed = 0.85;
    private double _cachedDangerThreshold = 0.0;
    private bool _isPlayerPlaying = false;
    private AudioEngine? _audioEngine;
    private bool _isShuttingDown = false;
    #endregion

    #region Dependency Properties
    public static readonly DependencyProperty ChannelHeightProperty =
        DependencyProperty.Register(nameof(ChannelHeight), typeof(double), typeof(VuMeter),
            new FrameworkPropertyMetadata(15.0, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutPropertyChanged));

    public double ChannelHeight
    {
        get { return (double)GetValue(ChannelHeightProperty); }
        set { SetValue(ChannelHeightProperty, value); }
    }

    public static readonly DependencyProperty ChannelSpacingProperty =
        DependencyProperty.Register(nameof(ChannelSpacing), typeof(double), typeof(VuMeter),
            new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutPropertyChanged));

    public double ChannelSpacing
    {
        get { return (double)GetValue(ChannelSpacingProperty); }
        set { SetValue(ChannelSpacingProperty, value); }
    }

    public static readonly DependencyProperty ShowLabelsProperty =
        DependencyProperty.Register(nameof(ShowLabels), typeof(bool), typeof(VuMeter),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutPropertyChanged));

    public bool ShowLabels
    {
        get { return (bool)GetValue(ShowLabelsProperty); }
        set { SetValue(ShowLabelsProperty, value); }
    }

    public static readonly DependencyProperty DecaySpeedProperty =
        DependencyProperty.Register(nameof(DecaySpeed), typeof(double), typeof(VuMeter),
            new FrameworkPropertyMetadata(0.85, FrameworkPropertyMetadataOptions.None, OnDecaySpeedChanged));

    public double DecaySpeed
    {
        get { return (double)GetValue(DecaySpeedProperty); }
        set { SetValue(DecaySpeedProperty, value); }
    }

    public static readonly DependencyProperty PeakBrushProperty =
        DependencyProperty.Register(nameof(PeakBrush), typeof(Brush), typeof(VuMeter),
            new FrameworkPropertyMetadata(null));

    public Brush? PeakBrush
    {
        get { return (Brush?)GetValue(PeakBrushProperty); }
        set { SetValue(PeakBrushProperty, value); }
    }

    public static readonly DependencyProperty GradientStartColorProperty =
        DependencyProperty.Register(nameof(GradientStartColor), typeof(Color), typeof(VuMeter),
            new FrameworkPropertyMetadata(Color.FromRgb(44, 8, 106), FrameworkPropertyMetadataOptions.AffectsRender));

    public Color GradientStartColor
    {
        get { return (Color)GetValue(GradientStartColorProperty); }
        set { SetValue(GradientStartColorProperty, value); }
    }

    public static readonly DependencyProperty GradientEndColorProperty =
        DependencyProperty.Register(nameof(GradientEndColor), typeof(Color), typeof(VuMeter),
            new FrameworkPropertyMetadata(Colors.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public Color GradientEndColor
    {
        get { return (Color)GetValue(GradientEndColorProperty); }
        set { SetValue(GradientEndColorProperty, value); }
    }

    public static readonly DependencyProperty ClippingColorProperty =
        DependencyProperty.Register(nameof(ClippingColor), typeof(Color), typeof(VuMeter),
            new FrameworkPropertyMetadata(Colors.Red));

    public Color ClippingColor
    {
        get { return (Color)GetValue(ClippingColorProperty); }
        set { SetValue(ClippingColorProperty, value); }
    }

    public static readonly DependencyProperty WarningThresholdProperty =
        DependencyProperty.Register(nameof(WarningThreshold), typeof(double), typeof(VuMeter),
            new FrameworkPropertyMetadata(-6.0));

    public double WarningThreshold
    {
        get { return (double)GetValue(WarningThresholdProperty); }
        set { SetValue(WarningThresholdProperty, value); }
    }

    public static readonly DependencyProperty DangerThresholdProperty =
        DependencyProperty.Register(nameof(DangerThreshold), typeof(double), typeof(VuMeter),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.None, OnDangerThresholdChanged));

    public double DangerThreshold
    {
        get { return (double)GetValue(DangerThresholdProperty); }
        set { SetValue(DangerThresholdProperty, value); }
    }

    public static readonly DependencyProperty ScaleBrushProperty =
        DependencyProperty.Register(nameof(ScaleBrush), typeof(Brush), typeof(VuMeter),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush ScaleBrush
    {
        get { return (Brush)GetValue(ScaleBrushProperty); }
        set { SetValue(ScaleBrushProperty, value); }
    }

    public static readonly DependencyProperty ChannelCountProperty =
        DependencyProperty.Register(nameof(ChannelCount), typeof(int), typeof(VuMeter),
            new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutPropertyChanged, CoerceChannelCount));

    public int ChannelCount
    {
        get { return (int)GetValue(ChannelCountProperty); }
        set { SetValue(ChannelCountProperty, value); }
    }

    private static object CoerceChannelCount(DependencyObject d, object value)
    {
        int v = (int)value;
        return v < 1 ? 1 : v;
    }

    private static void OnDecaySpeedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        VuMeter vu = (VuMeter)d;
        vu._cachedDecaySpeed = (double)e.NewValue;
    }

    private static void OnDangerThresholdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        VuMeter vu = (VuMeter)d;
        vu._cachedDangerThreshold = (double)e.NewValue;
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        VuMeter vu = (VuMeter)d;
        vu.SafeUpdateLayout();
    }
    #endregion

    #region Constructor
    public VuMeter()
    {
        try
        {
            if (App.AppHost?.Services != null)
            {
                _logger = App.AppHost.Services.GetRequiredService<ILogger<VuMeter>>();
            }
            else
            {
                _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<VuMeter>.Instance;
            }
        }
        catch
        {
            _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<VuMeter>.Instance;
            _isShuttingDown = true;
        }

        _animationTimer = new System.Threading.Timer(
            AnimationTimer_Tick,
            null,
            Timeout.Infinite,
            DefaultUpdateInterval);

        DefaultStyleKey = typeof(VuMeter);

        _cachedDecaySpeed = DecaySpeed;
        _cachedDangerThreshold = DangerThreshold;

        _channelCount = ChannelCount;
        _channelLevels = Enumerable.Repeat(MinDbValue, _channelCount).ToArray();
    }
    #endregion

    #region Public Methods
    public void RegisterSoundPlayer(ISpectrumPlayer soundPlayer)
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (_soundPlayer != null)
        {
            _soundPlayer.PropertyChanged -= SoundPlayer_PropertyChanged;
            _soundPlayer.OnFftCalculated -= SoundPlayer_OnFftCalculated;
        }

        _soundPlayer = soundPlayer;
        if (soundPlayer is AudioEngine audioEngine)
        {
            _audioEngine = audioEngine;
        }

        if (_soundPlayer != null)
        {
            _soundPlayer.PropertyChanged += SoundPlayer_PropertyChanged;
            _soundPlayer.OnFftCalculated += SoundPlayer_OnFftCalculated;
            _isPlayerPlaying = _soundPlayer.IsPlaying;
            if (_soundPlayer.IsPlaying)
            {
                _animationTimer.Change(0, DefaultUpdateInterval);
            }
            else
            {
                _animationTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            _logger.LogInformation("VuMeter: Registered sound player");
        }
    }

    public void UnregisterSoundPlayer()
    {
        if (_soundPlayer != null)
        {
            _soundPlayer.PropertyChanged -= SoundPlayer_PropertyChanged;
            _soundPlayer.OnFftCalculated -= SoundPlayer_OnFftCalculated;
            _soundPlayer = null;
            _audioEngine = null;
            _isPlayerPlaying = false;
            _animationTimer.Change(Timeout.Infinite, Timeout.Infinite);

            lock (_lockObject)
            {
                for (int i = 0; i < _channelLevels.Length; i++)
                {
                    _channelLevels[i] = MinDbValue;
                }
            }
            UpdateVuBars();
            _logger.LogInformation("VuMeter: Sound player unregistered");
        }
    }
    #endregion

    #region Template Overrides
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _vuCanvas = GetTemplateChild("PART_VuCanvas") as Canvas;
        if (_vuCanvas != null)
        {
            _vuCanvas.SizeChanged += VuCanvas_SizeChanged;
        }
        SafeUpdateLayout();
    }

    protected override void OnTemplateChanged(ControlTemplate oldTemplate, ControlTemplate newTemplate)
    {
        base.OnTemplateChanged(oldTemplate, newTemplate);
        if (_vuCanvas != null)
        {
            _vuCanvas.SizeChanged -= VuCanvas_SizeChanged;
            _vuCanvas = null;
        }
    }
    #endregion

    #region Event Overrides
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        SafeUpdateLayout();
    }
    #endregion

    #region Private Layout / Update Methods
    private void SafeUpdateLayout()
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(UpdateVuLayout));
            return;
        }
        UpdateVuLayout();
    }

    private void UpdateVuLayout()
    {
        if (_vuCanvas == null || _vuCanvas.RenderSize.Width < 1 || _vuCanvas.RenderSize.Height < 1)
        {
            return;
        }

        _vuCanvas.Children.Clear();

        _channelCount = ChannelCount;
        if (_channelLevels.Length != _channelCount)
        {
            _channelLevels = Enumerable.Repeat(MinDbValue, _channelCount).ToArray();
        }

        double canvasWidth = _vuCanvas.RenderSize.Width;
        double canvasHeight = _vuCanvas.RenderSize.Height;
        double labelHeight = ShowLabels ? 15.0 : 0.0;
        double availableHeight = canvasHeight - labelHeight;
        double channelHeightDynamic = (availableHeight - ChannelSpacing * (_channelCount - 1)) / _channelCount;
        // Remove cap so each channel uses full proportional height
        double perChannelHeight = channelHeightDynamic;

        CreateChannelBars(canvasWidth, perChannelHeight);

        if (ShowLabels)
        {
            double scaleTopOffset = perChannelHeight * _channelCount + ChannelSpacing * (_channelCount - 1);
            CreateScaleMarkings(canvasWidth, scaleTopOffset);
        }
    }

    private void CreateScaleMarkings(double canvasWidth, double topOffset)
    {
        if (_vuCanvas == null)
        {
            return;
        }

        // Create dB scale markings from -60dB to +10dB in 10dB increments
        for (double db = MinDbValue; db <= MaxDbValue; db += 10)
        {
            double position = (db - MinDbValue) / DbRange * canvasWidth;

            // Create tick mark
            Line tickLine = new Line
            {
                X1 = position,
                Y1 = topOffset,
                X2 = position,
                Y2 = topOffset + 5,
                Stroke = ScaleBrush,
                StrokeThickness = 1
            };
            _vuCanvas.Children.Add(tickLine);

            // Create label
            TextBlock label = new TextBlock
            {
                Text = db == 0 ? "0" : db.ToString("+0;-0", CultureInfo.InvariantCulture),
                FontSize = 9,
                Foreground = ScaleBrush
            };
            Canvas.SetLeft(label, position - 8);
            Canvas.SetTop(label, topOffset + 6);
            _vuCanvas.Children.Add(label);
        }
    }

    private void CreateChannelBars(double canvasWidth, double channelHeight)
    {
        _channelBars.Clear();
        for (int i = 0; i < _channelCount; i++)
        {
            Rectangle bar = new Rectangle
            {
                Width = 0,
                Height = channelHeight,
                Fill = CreateGradientBrush(channelHeight, MinDbValue)
            };
            double top = i * (channelHeight + ChannelSpacing);
            Canvas.SetLeft(bar, 0);
            Canvas.SetTop(bar, top);
            _vuCanvas!.Children.Add(bar);
            _channelBars.Add(bar);

            if (ShowLabels)
            {
                string labelText = _channelCount == 2 ? (i == 0 ? "L" : "R") : $"Ch{i + 1}";
                TextBlock channelLabel = new TextBlock
                {
                    Text = labelText,
                    FontSize = 10,
                    Foreground = ScaleBrush,
                    FontWeight = FontWeights.Bold
                };
                Canvas.SetLeft(channelLabel, -25);
                Canvas.SetTop(channelLabel, top + (channelHeight / 2) - 6);
                _vuCanvas.Children.Add(channelLabel);
            }
        }
    }

    private void UpdateVuBars()
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(UpdateVuBarsInternal));
            return;
        }
        UpdateVuBarsInternal();
    }

    private void UpdateVuBarsInternal()
    {
        if (_vuCanvas == null || _channelBars.Count == 0)
        {
            return;
        }

        double canvasWidth = _vuCanvas.RenderSize.Width;
        double[] levels;
        lock (_lockObject)
        {
            levels = _channelLevels.ToArray();
        }
        int count = System.Math.Min(levels.Length, _channelBars.Count);
        for (int i = 0; i < count; i++)
        {
            double level = levels[i];
            double width = System.Math.Max(0, System.Math.Min(canvasWidth, (level - MinDbValue) / DbRange * canvasWidth));
            Rectangle bar = _channelBars[i];
            bar.Width = width;
            bar.Fill = CreateGradientBrush(bar.Height, level);
        }
    }

    private void UpdateAudioLevels()
    {
        if (_isShuttingDown || !_isPlayerPlaying || _audioEngine == null)
        {
            return;
        }

        try
        {
            double[] newLevels;
            if (_audioEngine is IChannelLevelProvider provider && provider.TryGetChannelDecibelLevels(out double[] multi))
            {
                newLevels = multi;
            }
            else
            {
                // Fallback stereo method
                (double leftDb, double rightDb) = _audioEngine.GetStereoDecibelLevels();
                newLevels = Enumerable.Repeat(MinDbValue, _channelCount).ToArray();
                if (_channelCount >= 1)
                {
                    newLevels[0] = leftDb;
                }

                if (_channelCount >= 2)
                {
                    newLevels[1] = rightDb;
                }
            }

            lock (_lockObject)
            {
                for (int i = 0; i < _channelCount; i++)
                {
                    double v = i < newLevels.Length ? newLevels[i] : MinDbValue;
                    _channelLevels[i] = System.Math.Max(MinDbValue, System.Math.Min(MaxDbValue, v));
                }
            }
        }
        catch (System.Exception ex)
        {
            if (!_isShuttingDown)
            {
                _logger.LogDebug(ex, "Error in UpdateAudioLevels: {Message}", ex.Message);
            }
        }
    }
    #endregion

    #region Event Handlers
    private void SoundPlayer_OnFftCalculated(float[] fftData)
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (_isPlayerPlaying && _soundPlayer != null && _soundPlayer.IsPlaying)
        {
            UpdateAudioLevels();
        }
    }

    private void SoundPlayer_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (e.PropertyName == nameof(ISpectrumPlayer.IsPlaying) && _soundPlayer != null)
        {
            bool wasPlaying = _isPlayerPlaying;
            _isPlayerPlaying = _soundPlayer.IsPlaying;
            if (_soundPlayer.IsPlaying)
            {
                _animationTimer.Change(0, DefaultUpdateInterval);
            }
            else
            {
                _animationTimer.Change(Timeout.Infinite, Timeout.Infinite);
                lock (_lockObject)
                {
                    for (int i = 0; i < _channelLevels.Length; i++)
                    {
                        _channelLevels[i] = MinDbValue;
                    }
                }
                UpdateVuBars();
            }
        }
    }

    private void AnimationTimer_Tick(object? state)
    {
        if (_isShuttingDown || !_isPlayerPlaying)
        {
            return;
        }

        try
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateAudioLevels();
                UpdateVuBars();
            }), DispatcherPriority.Background);
        }
        catch (System.Exception ex)
        {
            if (!_isShuttingDown)
            {
                _logger.LogDebug(ex, "Error in VuMeter animation timer: {Message}", ex.Message);
            }
        }
    }

    private void VuCanvas_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        SafeUpdateLayout();
    }
    #endregion

    #region Static Constructor
    static VuMeter()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(VuMeter), new FrameworkPropertyMetadata(typeof(VuMeter)));
    }
    #endregion

    #region Gradient Brush Creator
    private Brush CreateGradientBrush(double barHeight, double dbLevel)
    {
        LinearGradientBrush gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        gradient.GradientStops.Add(new GradientStop(GradientStartColor, 0.0));
        gradient.GradientStops.Add(new GradientStop(GradientEndColor, 1.0));
        gradient.Freeze();
        return gradient;
    }
    #endregion
}
