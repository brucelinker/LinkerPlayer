using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LinkerPlayer.Audio;

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
    private readonly DispatcherTimer _animationTimer;
    private Canvas? _vuCanvas;
    private ISpectrumPlayer? _soundPlayer;
    private readonly ILogger<VuMeter> _logger;
    private Rectangle? _leftChannelBar;
    private Rectangle? _rightChannelBar;
    private double _leftLevel;
    private double _rightLevel;
    private readonly Brush _normalBrush = new SolidColorBrush(Color.FromRgb(0, 255, 0));
    private readonly Brush _warningBrush = new SolidColorBrush(Color.FromRgb(255, 255, 0));
    private readonly Brush _dangerBrush = new SolidColorBrush(Color.FromRgb(255, 0, 0));
    private readonly object _lockObject = new object();

    // Cached property values to avoid cross-thread access
    private double _cachedDecaySpeed = 0.85;
    private bool _isPlayerPlaying = false;
    private AudioEngine? _audioEngine;
    private bool _isShuttingDown = false;
    #endregion

    #region Dependency Properties
    public static readonly DependencyProperty ChannelHeightProperty =
        DependencyProperty.Register(nameof(ChannelHeight), typeof(double), typeof(VuMeter),
            new UIPropertyMetadata(15.0, OnLayoutPropertyChanged));

    public double ChannelHeight
    {
        get => (double)GetValue(ChannelHeightProperty);
        set => SetValue(ChannelHeightProperty, value);
    }

    public static readonly DependencyProperty ChannelSpacingProperty =
        DependencyProperty.Register(nameof(ChannelSpacing), typeof(double), typeof(VuMeter),
            new UIPropertyMetadata(3.0, OnLayoutPropertyChanged));

    public double ChannelSpacing
    {
        get => (double)GetValue(ChannelSpacingProperty);
        set => SetValue(ChannelSpacingProperty, value);
    }

    public static readonly DependencyProperty ShowLabelsProperty =
        DependencyProperty.Register(nameof(ShowLabels), typeof(bool), typeof(VuMeter),
            new UIPropertyMetadata(true, OnLayoutPropertyChanged));

    public bool ShowLabels
    {
        get => (bool)GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
    }

    public static readonly DependencyProperty DecaySpeedProperty =
        DependencyProperty.Register(nameof(DecaySpeed), typeof(double), typeof(VuMeter),
            new UIPropertyMetadata(0.85, OnDecaySpeedChanged));

    public double DecaySpeed
    {
        get => (double)GetValue(DecaySpeedProperty);
        set => SetValue(DecaySpeedProperty, value);
    }

    private static void OnDecaySpeedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VuMeter vuMeter)
        {
            vuMeter._cachedDecaySpeed = (double)e.NewValue;
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VuMeter vuMeter)
            vuMeter.SafeUpdateLayout();
    }
    #endregion

    #region Constructor
    public VuMeter()
    {
        try
        {
            // Check if App.AppHost is available before trying to get logger
            if (App.AppHost?.Services != null)
            {
                _logger = App.AppHost.Services.GetRequiredService<ILogger<VuMeter>>();
            }
            else
            {
                // Create a null logger to avoid exceptions during shutdown
                _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<VuMeter>.Instance;
            }
        }
        catch (Exception)
        {
            // If DI container is disposed, use null logger
            _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<VuMeter>.Instance;
            _isShuttingDown = true;
        }

        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(DefaultUpdateInterval)
        };
        _animationTimer.Tick += AnimationTimer_Tick;

        // Freeze brushes for performance
        _normalBrush.Freeze();
        _warningBrush.Freeze();
        _dangerBrush.Freeze();

        // Set default template
        DefaultStyleKey = typeof(VuMeter);

        // Initialize cached values
        _cachedDecaySpeed = DecaySpeed;

        // Initialize levels to minimum
        _leftLevel = MinDbValue;
        _rightLevel = MinDbValue;
    }
    #endregion

    #region Public Methods
    public void RegisterSoundPlayer(ISpectrumPlayer soundPlayer)
    {
        if (_isShuttingDown) return;

        if (_soundPlayer != null)
        {
            _soundPlayer.PropertyChanged -= SoundPlayer_PropertyChanged;
            _soundPlayer.OnFftCalculated -= SoundPlayer_OnFftCalculated;
        }

        _soundPlayer = soundPlayer;

        // Try to get the AudioEngine instance
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
                _animationTimer.Start();
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

            // Force immediate cleanup
            _animationTimer.Stop();

            lock (_lockObject)
            {
                _leftLevel = MinDbValue;
                _rightLevel = MinDbValue;
            }

            if (!_isShuttingDown)
            {
                UpdateVuBars(); // Final update to clear bars
                _logger.LogInformation("VuMeter: Sound player unregistered");
            }
        }
    }
    #endregion

    #region Template Overrides
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _vuCanvas = GetTemplateChild("PART_VuCanvas") as Canvas;
        if (_vuCanvas != null)
            _vuCanvas.SizeChanged += VuCanvas_SizeChanged;
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

    #region Private Methods
    private void SafeUpdateLayout()
    {
        if (_isShuttingDown) return;

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
            return;

        _vuCanvas.Children.Clear();

        double canvasWidth = _vuCanvas.RenderSize.Width;
        double canvasHeight = _vuCanvas.RenderSize.Height;
        double labelHeight = ShowLabels ? 15 : 0;
        double availableHeight = canvasHeight - labelHeight;
        double channelHeight = Math.Min(ChannelHeight, (availableHeight - ChannelSpacing) / 2);

        // Create scale markings and labels
        if (ShowLabels)
        {
            CreateScaleMarkings(canvasWidth, labelHeight);
        }

        // Create channel bars
        CreateChannelBars(canvasWidth, channelHeight, labelHeight);
    }

    private void CreateScaleMarkings(double canvasWidth, double labelHeight)
    {
        if (_vuCanvas == null) return;

        // Create dB scale markings from -60dB to +10dB in 10dB increments
        for (double db = MinDbValue; db <= MaxDbValue; db += 10)
        {
            double position = (db - MinDbValue) / DbRange * canvasWidth;

            // Create tick mark
            var tickLine = new Line
            {
                X1 = position,
                Y1 = 0,
                X2 = position,
                Y2 = 5,
                Stroke = Brushes.Gray,
                StrokeThickness = 1
            };
            _vuCanvas.Children.Add(tickLine);

            // Create label
            var label = new TextBlock
            {
                Text = db == 0 ? "0" : db.ToString("+0;-0", CultureInfo.InvariantCulture),
                FontSize = 9,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            Canvas.SetLeft(label, position - 8);
            Canvas.SetTop(label, 6);
            _vuCanvas.Children.Add(label);
        }
    }

    private void CreateChannelBars(double canvasWidth, double channelHeight, double labelOffset)
    {
        if (_vuCanvas == null) return;

        // Left channel bar
        _leftChannelBar = new Rectangle
        {
            Width = 0,
            Height = channelHeight,
            Fill = _normalBrush
        };
        Canvas.SetLeft(_leftChannelBar, 0);
        Canvas.SetTop(_leftChannelBar, labelOffset);
        _vuCanvas.Children.Add(_leftChannelBar);

        // Right channel bar  
        _rightChannelBar = new Rectangle
        {
            Width = 0,
            Height = channelHeight,
            Fill = _normalBrush
        };
        Canvas.SetLeft(_rightChannelBar, 0);
        Canvas.SetTop(_rightChannelBar, labelOffset + channelHeight + ChannelSpacing);
        _vuCanvas.Children.Add(_rightChannelBar);

        // Channel labels
        if (ShowLabels)
        {
            var leftLabel = new TextBlock
            {
                Text = "L",
                FontSize = 10,
                Foreground = Brushes.Gray,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(leftLabel, -15);
            Canvas.SetTop(leftLabel, labelOffset + (channelHeight / 2) - 6);
            _vuCanvas.Children.Add(leftLabel);

            var rightLabel = new TextBlock
            {
                Text = "R",
                FontSize = 10,
                Foreground = Brushes.Gray,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(rightLabel, -15);
            Canvas.SetTop(rightLabel, labelOffset + channelHeight + ChannelSpacing + (channelHeight / 2) - 6);
            _vuCanvas.Children.Add(rightLabel);
        }
    }

    private void UpdateVuBars()
    {
        if (_isShuttingDown) return;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(UpdateVuBarsInternal));
            return;
        }
        UpdateVuBarsInternal();
    }

    private void UpdateVuBarsInternal()
    {
        if (_vuCanvas == null || _leftChannelBar == null || _rightChannelBar == null)
            return;

        double canvasWidth = _vuCanvas.RenderSize.Width;
        double leftLevel, rightLevel;

        lock (_lockObject)
        {
            leftLevel = _leftLevel;
            rightLevel = _rightLevel;
        }

        // Update left channel
        double leftWidth = Math.Max(0, Math.Min(canvasWidth, (leftLevel - MinDbValue) / DbRange * canvasWidth));
        _leftChannelBar.Width = leftWidth;
        _leftChannelBar.Fill = GetLevelBrush(leftLevel);

        // Update right channel  
        double rightWidth = Math.Max(0, Math.Min(canvasWidth, (rightLevel - MinDbValue) / DbRange * canvasWidth));
        _rightChannelBar.Width = rightWidth;
        _rightChannelBar.Fill = GetLevelBrush(rightLevel);
    }

    private Brush GetLevelBrush(double dbLevel)
    {
        return dbLevel switch
        {
            >= 0 => _dangerBrush,      // 0dB and above - red
            >= -6 => _warningBrush,    // -6dB to 0dB - yellow  
            _ => _normalBrush          // Below -6dB - green
        };
    }

    private void UpdateAudioLevels()
    {
        if (_isShuttingDown) return;

        // Only update levels when actually playing
        if (!_isPlayerPlaying || _audioEngine == null)
        {
            return;
        }

        try
        {
            // Get real audio levels from BASS
            double dbLevel = _audioEngine.GetDecibelLevel();

            // If we can't get a level, don't update
            if (double.IsNaN(dbLevel) || double.IsInfinity(dbLevel))
            {
                return;
            }

            lock (_lockObject)
            {
                // SIMPLIFIED: Just use the BASS dB values directly - no peak hold, no decay during playback
                // This is to test if the VU meter display works at all
                _leftLevel = dbLevel;
                _rightLevel = dbLevel + 1.0; // Slight difference for stereo simulation

                // Clamp to valid range
                _leftLevel = Math.Max(MinDbValue, Math.Min(MaxDbValue, _leftLevel));
                _rightLevel = Math.Max(MinDbValue, Math.Min(MaxDbValue, _rightLevel));

                // Debug logging
                if (!_isShuttingDown && DateTime.Now.Millisecond < 50) // Log roughly once per second
                {
                    _logger.LogDebug("VuMeter: BASS dB={DbLevel:F1} | Direct=L:{LeftLevel:F1},R:{RightLevel:F1}",
                        dbLevel, _leftLevel, _rightLevel);
                }
            }
        }
        catch (Exception ex)
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
        if (_isShuttingDown) return;

        // FIXED: Only update levels when actually playing AND add extra safety check
        if (_isPlayerPlaying && _soundPlayer != null && _soundPlayer.IsPlaying)
        {
            try
            {
                UpdateAudioLevels();
            }
            catch (Exception ex)
            {
                if (!_isShuttingDown)
                {
                    _logger.LogDebug(ex, "Error in VuMeter FFT event handler: {Message}", ex.Message);
                }
            }
        }
        else
        {
            // Log when we're getting FFT events but not playing
            if (!_isShuttingDown)
            {
                _logger.LogDebug("VuMeter: Ignoring FFT event - not playing (isPlayerPlaying={IsPlayerPlaying}, soundPlayer.IsPlaying={SoundPlayerIsPlaying})",
                    _isPlayerPlaying, _soundPlayer?.IsPlaying);
            }
        }
    }

    private void SoundPlayer_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isShuttingDown) return;

        if (e.PropertyName == "IsPlaying" && _soundPlayer != null)
        {
            bool wasPlaying = _isPlayerPlaying;
            _isPlayerPlaying = _soundPlayer.IsPlaying;

            _logger.LogDebug("VuMeter: Player state changed from {WasPlaying} to {IsPlaying}", wasPlaying, _isPlayerPlaying);

            if (_soundPlayer.IsPlaying && !_animationTimer.IsEnabled)
            {
                _animationTimer.Start();
                _logger.LogDebug("VuMeter: Player started, timer started");
            }
            else if (!_soundPlayer.IsPlaying)
            {
                _logger.LogDebug("VuMeter: Player stopped, current levels L:{LeftLevel:F1}, R:{RightLevel:F1}", _leftLevel, _rightLevel);

                // FIXED: Immediately clear levels when player stops - don't rely on decay
                lock (_lockObject)
                {
                    _leftLevel = MinDbValue;
                    _rightLevel = MinDbValue;
                }

                // Stop the timer - no need for decay animation
                _animationTimer.Stop();

                // Update display immediately
                UpdateVuBars();

                _logger.LogDebug("VuMeter: Levels immediately set to minimum when stopped");
            }
        }
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
        {
            _animationTimer.Stop();
            return;
        }

        try
        {
            // Timer should only run during playback for smooth updates
            if (_isPlayerPlaying)
            {
                UpdateVuBars();
            }
            else
            {
                // If timer is running but not playing, stop it
                _animationTimer.Stop();
                _logger.LogDebug("VuMeter: Timer stopped - not playing");
            }
        }
        catch (Exception ex)
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
        DefaultStyleKeyProperty.OverrideMetadata(typeof(VuMeter),
            new FrameworkPropertyMetadata(typeof(VuMeter)));
    }
    #endregion
}