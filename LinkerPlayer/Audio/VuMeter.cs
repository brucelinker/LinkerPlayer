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

// L Left R Right [Stereo]
// FL Front Left FR Front Right SL Surround Left SR Surround Right [QUAD]
// FL Front Left FR Front Right FC Front Center LFE Low-Frequency Effects SL Surround Left SR Surround Right [5.1]
// FL Front Left FR Front Right FC Front Center LFE Low-Frequency Effects SL Surround Left SR Surround Right SBL Surround Back Left SBR Surround Back Right [7.1]

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
    // Replaced System.Threading.Timer with DispatcherTimer to avoid cross-thread marshal cost each tick.
    private readonly DispatcherTimer _uiTimer;
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

    // Cached gradient brush
    private LinearGradientBrush _baseGradient = null!;
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
            new FrameworkPropertyMetadata(Color.FromRgb(44, 8, 106), FrameworkPropertyMetadataOptions.AffectsRender, OnGradientChanged));

    public Color GradientStartColor
    {
        get { return (Color)GetValue(GradientStartColorProperty); }
        set { SetValue(GradientStartColorProperty, value); }
    }

    public static readonly DependencyProperty GradientEndColorProperty =
        DependencyProperty.Register(nameof(GradientEndColor), typeof(Color), typeof(VuMeter),
            new FrameworkPropertyMetadata(Colors.Black, FrameworkPropertyMetadataOptions.AffectsRender, OnGradientChanged));

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

    public static readonly DependencyProperty ChannelLabelWidthProperty =
        DependencyProperty.Register(nameof(ChannelLabelWidth), typeof(double), typeof(VuMeter),
            new FrameworkPropertyMetadata(30.0, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutPropertyChanged));

    public double ChannelLabelWidth
    {
        get { return (double)GetValue(ChannelLabelWidthProperty); }
        set { SetValue(ChannelLabelWidthProperty, value); }
    }

    public static readonly DependencyProperty ScaleLabelHeightProperty =
        DependencyProperty.Register(nameof(ScaleLabelHeight), typeof(double), typeof(VuMeter),
            new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutPropertyChanged));

    public double ScaleLabelHeight
    {
        get { return (double)GetValue(ScaleLabelHeightProperty); }
        set { SetValue(ScaleLabelHeightProperty, value); }
    }

    public static readonly DependencyProperty ChannelLabelFontSizeProperty =
        DependencyProperty.Register(nameof(ChannelLabelFontSize), typeof(double), typeof(VuMeter),
            new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutPropertyChanged));

    public double ChannelLabelFontSize
    {
        get { return (double)GetValue(ChannelLabelFontSizeProperty); }
        set { SetValue(ChannelLabelFontSizeProperty, value); }
    }

    public static readonly DependencyProperty ScaleLabelFontSizeProperty =
        DependencyProperty.Register(nameof(ScaleLabelFontSize), typeof(double), typeof(VuMeter),
            new FrameworkPropertyMetadata(9.0, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutPropertyChanged));

    public double ScaleLabelFontSize
    {
        get { return (double)GetValue(ScaleLabelFontSizeProperty); }
        set { SetValue(ScaleLabelFontSizeProperty, value); }
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

    private static void OnGradientChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        VuMeter vu = (VuMeter)d;
        vu.CreateOrUpdateGradient();
        vu.UpdateVuBars();
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

        _uiTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(DefaultUpdateInterval)
        };
        _uiTimer.Tick += UiTimer_Tick;

        DefaultStyleKey = typeof(VuMeter);

        _cachedDecaySpeed = DecaySpeed;
        _cachedDangerThreshold = DangerThreshold;

        _channelCount = ChannelCount;
        _channelLevels = Enumerable.Repeat(MinDbValue, _channelCount).ToArray();

        CreateOrUpdateGradient();
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
            // Channel count will be updated via PropertyChanged when a file loads
        }

        if (_soundPlayer != null)
        {
            _soundPlayer.PropertyChanged += SoundPlayer_PropertyChanged;
            _soundPlayer.OnFftCalculated += SoundPlayer_OnFftCalculated;
            _isPlayerPlaying = _soundPlayer.IsPlaying;
            if (_soundPlayer.IsPlaying)
            {
                StartTimer();
            }
            else
            {
                StopTimer();
            }
            _logger.LogDebug("VuMeter: Registered sound player");
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
            StopTimer();

            lock (_lockObject)
            {
                for (int i = 0; i < _channelLevels.Length; i++)
                {
                    _channelLevels[i] = MinDbValue;
                }
            }
            UpdateVuBars();
            _logger.LogDebug("VuMeter: Sound player unregistered");
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

    public void SafeUpdateLayout()
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

    #region Private Layout / Update Methods
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
        double labelHeight = ShowLabels ? ScaleLabelHeight : 0.0;
        double availableHeight = canvasHeight - labelHeight;
        double perChannelHeight = (_channelCount > 0)
            ? (availableHeight - ChannelSpacing * (_channelCount - 1)) / _channelCount
            : 0;
        // Always use dynamic per-channel height, ignore ChannelHeight property
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

        double labelWidth = ShowLabels ? ChannelLabelWidth : 0.0;
        double availableBarWidth = canvasWidth - labelWidth;

        // Create dB scale markings from -60dB to +10dB in 10dB increments
        for (double db = MinDbValue; db <= MaxDbValue; db += 10)
        {
            double position = labelWidth + (db - MinDbValue) / DbRange * availableBarWidth;

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
                FontSize = ScaleLabelFontSize,
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
        string[] labels = _channelCount switch
        {
            2 => new[] { "L", "R" },
            4 => new[] { "FL", "FR", "SL", "SR" },
            6 => new[] { "FL", "FR", "FC", "LFE", "SL", "SR" },
            8 => new[] { "FL", "FR", "FC", "LFE", "BL", "BR", "SL", "SR" },
            _ => Enumerable.Range(1, _channelCount).Select(i => $"Ch{i}").ToArray()
        };

        // Reserve space for labels on the left
        double labelWidth = ShowLabels ? ChannelLabelWidth : 0.0;
        double barStartX = labelWidth;

        for (int i = 0; i < _channelCount; i++)
        {
            Rectangle bar = new Rectangle
            {
                Width = 0,
                Height = channelHeight,
                Fill = _baseGradient
            };
            double top = i * (channelHeight + ChannelSpacing);
            Canvas.SetLeft(bar, barStartX);
            Canvas.SetTop(bar, top);
            _vuCanvas!.Children.Add(bar);
            _channelBars.Add(bar);

            if (ShowLabels)
            {
                string labelText = i < labels.Length ? labels[i] : $"Ch{i + 1}";
                TextBlock channelLabel = new TextBlock
                {
                    Text = labelText,
                    FontSize = ChannelLabelFontSize,
                    Foreground = ScaleBrush,
                    FontWeight = FontWeights.Light
                };
                Canvas.SetLeft(channelLabel, 2);
                Canvas.SetTop(channelLabel, top + (channelHeight / 2) - (ChannelLabelFontSize / 2));
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
            Dispatcher.BeginInvoke(new Action(UpdateVuBarsInternal), DispatcherPriority.Render);
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
        double labelWidth = ShowLabels ? ChannelLabelWidth : 0.0;
        double availableBarWidth = canvasWidth - labelWidth;

        double[] levels;
        lock (_lockObject)
        {
            levels = _channelLevels.ToArray();
        }
        int count = System.Math.Min(levels.Length, _channelBars.Count);
        for (int i = 0; i < count; i++)
        {
            double level = levels[i];
            double width = System.Math.Max(0, System.Math.Min(availableBarWidth, (level - MinDbValue) / DbRange * availableBarWidth));
            Rectangle bar = _channelBars[i];
            bar.Width = width;
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
            _isPlayerPlaying = _soundPlayer.IsPlaying;
            if (_soundPlayer.IsPlaying)
            {
                StartTimer();
            }
            else
            {
                StopTimer();
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

        // Listen for ChannelCount changes from AudioEngine (when new tracks load with different channel counts)
        if (e.PropertyName == nameof(AudioEngine.ChannelCount) && _audioEngine != null)
        {
            // PropertyChanged may fire from a background thread, so marshal to UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SoundPlayer_PropertyChanged(sender, e)));
                return;
            }

            int newChannelCount = _audioEngine.ChannelCount;
            if (newChannelCount != ChannelCount && newChannelCount > 0)
            {
                _logger.LogDebug("VuMeter: AudioEngine channel count changed from {OldCount} to {NewCount}", ChannelCount, newChannelCount);
                ChannelCount = newChannelCount;
                SafeUpdateLayout();
            }
        }
    }

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        if (_isShuttingDown || !_isPlayerPlaying)
        {
            return;
        }

        UpdateAudioLevels();
        UpdateVuBars();
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
    private void CreateOrUpdateGradient()
    {
        LinearGradientBrush gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        gradient.GradientStops.Add(new GradientStop(GradientStartColor, 0.0));
        gradient.GradientStops.Add(new GradientStop(GradientEndColor, 1.0));
        gradient.Freeze();
        _baseGradient = gradient;
        foreach (Rectangle bar in _channelBars)
        {
            bar.Fill = _baseGradient;
        }
    }
    #endregion

    #region Timer Helpers
    private void StartTimer()
    {
        if (!_uiTimer.IsEnabled)
        {
            _uiTimer.Interval = TimeSpan.FromMilliseconds(DefaultUpdateInterval);
            _uiTimer.Start();
        }
    }

    private void StopTimer()
    {
        if (_uiTimer.IsEnabled)
        {
            _uiTimer.Stop();
        }
    }
    #endregion
}
