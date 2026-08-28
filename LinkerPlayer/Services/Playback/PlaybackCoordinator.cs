using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using ManagedBass;
using Microsoft.Extensions.Logging;

namespace LinkerPlayer.Services.Playback;

public interface IPlaybackCoordinator
{
    PlaybackState PlaybackState { get; }
    PlaybackCursor? PlaybackCursor { get; }   // can be null only before any playlist is loaded

    // New unified method
    void SetSelection(string playlistName, int trackIndex, MediaFile track);

    void PlaySelected();           // plays current cursor
    void PlayTrack(string playlistName, int trackIndex, MediaFile track, double positionSeconds = 0);
    void Pause();
    void Resume();
    void Stop();
    void Seek(double positionSeconds);
    void Next();
    void Prev();
}

public sealed class PlaybackCoordinator : IPlaybackCoordinator, IRecipient<ShuffleModeMessage>
{
    private readonly IAudioEngine _audioEngine;
    private readonly ITrackNavigationService _trackNavigationService;
    private readonly IMusicLibrary _musicLibrary;
    private readonly ISettingsManager _settingsManager;
    private readonly ISharedDataModel _sharedDataModel;
    private readonly ILogger<PlaybackCoordinator> _logger;

    private readonly object _sync = new object();

    private int _suppressNextEngineStopped;

    private const int SkipSilenceMinimumDurationMs = 1000;

    private bool IsCrossfadeEnabled => _settingsManager.Settings.CrossfadeEnabled;
    private int FadeInMs => _settingsManager.Settings.CrossfadeFadeInMs;
    private int FadeOutMs => _settingsManager.Settings.CrossfadeFadeOutMs;
    private FadeCurveShape FadeCurveShape => _settingsManager.Settings.CrossfadeCurveShape;

    private bool SmoothStopFadeEnabled => _settingsManager.Settings.SmoothStopFadeEnabled;
    private int SmoothStopFadeMs => _settingsManager.Settings.SmoothStopFadeMs;

    // Crossfade (UI wiring later)
    private bool _crossfadeInProgress;
    private long _crossfadeVersion;
    private long _pendingCrossfadeVersion;
    private string? _pendingCrossfadePlaylistName;
    private int _pendingCrossfadeTrackIndex;
    private MediaFile? _pendingCrossfadeTrack;

    private System.Threading.Timer? _trailingSilenceTimer;

    private long _trailingSilenceTimerVersion;

    private sealed class TrailingSilenceTimerState
    {
        public required long TimerVersion { get; init; }
        public required string TrackId { get; init; }
        public required string TrackPath { get; init; }
        public required string PlaylistName { get; init; }
        public required int TrackIndex { get; init; }
        public required int DueMs { get; init; }
    }

    private TrailingSilenceTimerState? _trailingSilenceTimerState;

    private string? _currentTrackId;
    private double _currentTrackStartSeconds;

    private System.Threading.Timer? _eofWatchdogTimer;
    private long _eofWatchdogVersion;
    private bool _stopRequested;
    private string? _activePlaybackSourcePlaylistName;

    public PlaybackCoordinator(
        IAudioEngine audioEngine,
        ITrackNavigationService trackNavigationService,
        IMusicLibrary musicLibrary,
        ISettingsManager settingsManager,
        ISharedDataModel sharedDataModel,
       ILogger<PlaybackCoordinator> logger)
    {
        _audioEngine = audioEngine;
        _trackNavigationService = trackNavigationService;
        _musicLibrary = musicLibrary;
        _settingsManager = settingsManager;
        _sharedDataModel = sharedDataModel;
        _logger = logger;

        _audioEngine.OnPlaybackStopped += OnEnginePlaybackStopped;
        _audioEngine.OnTrackEnded += OnEngineTrackEnded;
        _audioEngine.OnCrossfadeCommitted += OnEngineCrossfadeCommitted;

        WeakReferenceMessenger.Default.Register(this);
    }

    public PlaybackState PlaybackState { get; private set; }

    public PlaybackCursor? PlaybackCursor { get; private set; }

    public void SetSelection(string playlistName, int trackIndex, MediaFile track)
    {
        if (playlistName == null)
            throw new ArgumentNullException(nameof(playlistName));
        if (track == null)
            throw new ArgumentNullException(nameof(track));

        _logger.LogInformation("SetSelection CALLED (Playlist={Playlist}, Index={Index}, TrackId={TrackId}, Title={Title})", playlistName, trackIndex, track.Id, track.Title);

        lock (_sync)
        {
            bool hasActivePlaybackSession = PlaybackCursor != null
                && (_audioEngine.IsPlaying
                    || PlaybackState == PlaybackState.Playing
                    || PlaybackState == PlaybackState.Paused
                    || _crossfadeInProgress);

            if (hasActivePlaybackSession)
            {
                _logger.LogDebug(
                    "SetSelection ignored while playback session is active. Keeping cursor at {PlaylistName}:{TrackIndex}",
                    PlaybackCursor!.PlaylistName,
                    PlaybackCursor.TrackIndex);
                return;
            }

            PlaybackCursor = new PlaybackCursor
            {
                PlaylistName = playlistName,
                TrackIndex = trackIndex,
                TrackId = track.Id
            };
            _logger.LogInformation("SetSelection: PlaybackCursor set to {PlaylistName}:{TrackIndex}", playlistName, trackIndex);
        }
    }

    private void SetPlaybackState(PlaybackState newState)
    {
        if (PlaybackState == newState)
            return;

        PlaybackState = newState;
        WeakReferenceMessenger.Default.Send(new PlaybackStateChangedMessage(PlaybackState));
    }

    private string? GetActivePlaybackSourcePlaylistName()
    {
        if (!string.IsNullOrWhiteSpace(_activePlaybackSourcePlaylistName))
        {
            return _activePlaybackSourcePlaylistName;
        }

        return PlaybackCursor?.PlaylistName;
    }

    private int ResolveCurrentIndex(IList<MediaFile> tracks, PlaybackCursor cursor)
    {
        if (tracks.Count == 0)
        {
            return -1;
        }

        if (!string.IsNullOrWhiteSpace(_currentTrackId))
        {
            int currentTrackIndex = tracks.ToList().FindIndex(t => string.Equals(t.Id, _currentTrackId, StringComparison.Ordinal));
            if (currentTrackIndex >= 0)
            {
                return currentTrackIndex;
            }
        }

        int cursorTrackIndex = tracks.ToList().FindIndex(t => string.Equals(t.Id, cursor.TrackId, StringComparison.Ordinal));
        if (cursorTrackIndex >= 0)
        {
            return cursorTrackIndex;
        }

        return Math.Clamp(cursor.TrackIndex, 0, Math.Max(0, tracks.Count - 1));
    }

    private (string PlaylistName, IList<MediaFile>? Tracks, int CurrentIndex) ResolveActiveSourceTracks()
    {
        string? playlistName = GetActivePlaybackSourcePlaylistName();
        if (string.IsNullOrWhiteSpace(playlistName))
        {
            return (string.Empty, null, -1);
        }

        int fallbackIndex = PlaybackCursor?.TrackIndex ?? 0;
        (IList<MediaFile>? tracks, int currentIndex) = ResolvePlaylistTracks(playlistName, fallbackIndex);
        if (tracks == null || tracks.Count == 0)
        {
            return (playlistName, null, currentIndex);
        }

        if (PlaybackCursor != null)
        {
            currentIndex = ResolveCurrentIndex(tracks, PlaybackCursor);
        }

        return (playlistName, tracks, currentIndex);
    }

    private MediaFile? GetTrackFromCursor(PlaybackCursor cursor)
    {
        (IList<MediaFile>? tracks, int _) = ResolvePlaylistTracks(cursor.PlaylistName, cursor.TrackIndex);
        if (tracks == null || tracks.Count == 0)
            return null;

        return tracks.FirstOrDefault(t => string.Equals(t.Id, cursor.TrackId, StringComparison.Ordinal));
    }

    public void PlaySelected()
    {
        lock (_sync)
        {
            if (PlaybackCursor == null)
            {
                _logger.LogWarning("PlaySelected ignored: no cursor/selection");
                return;
            }

            MediaFile? track = GetTrackFromCursor(PlaybackCursor); // helper you'll need
            if (track == null)
                return;

            PlayTrack(PlaybackCursor.PlaylistName, PlaybackCursor.TrackIndex, track);
        }
    }

    public void PlayTrack(string playlistName, int trackIndex, MediaFile? track, double positionSeconds = 0)
    {
        if (playlistName == null)
        {
            throw new ArgumentNullException(nameof(playlistName));
        }

        if (track == null)
        {
            throw new ArgumentNullException(nameof(track));
        }

        _logger.LogInformation("PlayTrack START (Playlist={Playlist}, Index={Index}, TrackId={TrackId}, Title={Title})", playlistName, trackIndex, track.Id, track.Title);

        if (string.IsNullOrWhiteSpace(playlistName))
        {
            _logger.LogWarning("PlayTrack called with empty playlistName for track '{TrackTitle}' ({TrackId}). Navigation will not work.", track.Title, track.Id);
        }

        lock (_sync)
        {
            _activePlaybackSourcePlaylistName = playlistName;

            PlaybackCursor = new PlaybackCursor { PlaylistName = playlistName, TrackIndex = trackIndex, TrackId = track.Id };
            _logger.LogInformation("PlayTrack: PlaybackCursor set to {PlaylistName}:{TrackIndex}", playlistName, trackIndex);

            // StopCore + AudioEngine.Stop can result in multiple OnPlaybackStopped notifications.
            _suppressNextEngineStopped += 2;
            StopCore();

            StopEofWatchdog("playtrack-start");
            StartEofWatchdog("playtrack");
            StopTrailingSilenceTimer("playtrack", null);

            _sharedDataModel.UpdateActiveTrack(track);
            WeakReferenceMessenger.Default.Send(new ActiveTrackChangedMessage(track));

            _audioEngine.PathToMusic = track.Path;

            double startSeconds = positionSeconds;
            int? leadingSilenceMs = track.LeadingSilenceMs;
            if (leadingSilenceMs.HasValue && leadingSilenceMs.Value >= SkipSilenceMinimumDurationMs)
            {
                double leadingSeconds = leadingSilenceMs.Value / 1000d;
                startSeconds = Math.Max(startSeconds, leadingSeconds);
                //_logger.LogDebug("SkipSilence: applying leading silence seek (TrackId={TrackId}, LeadingMs={LeadingMs}, StartSeconds={StartSeconds})", track.Id, leadingSilenceMs.Value, startSeconds);
            }

            _currentTrackId = track.Id;
            _currentTrackStartSeconds = startSeconds;

            _ = Task.Run(() => _audioEngine.Play(track.Path, startSeconds));
            _ = Task.Run(() => StartTrailingSilenceAfterTrackLoadedAsync(track, startSeconds));

            SetPlaybackState(PlaybackState.Playing);
        }

        StartEofWatchdog("playtrack");
    }

    private async Task StartTrailingSilenceAfterTrackLoadedAsync(MediaFile track, double startSeconds)
    {
        const int maxWaitMs = 5000;
        const int pollMs = 25;

        int waitedMs = 0;
        while (waitedMs < maxWaitMs)
        {
            if (string.Equals(_audioEngine.LoadedTrackPath, track.Path, StringComparison.OrdinalIgnoreCase) && _audioEngine.CurrentTrackLength > 0)
            {
                break;
            }

            await Task.Delay(pollMs).ConfigureAwait(false);
            waitedMs += pollMs;
        }

        lock (_sync)
        {
            if (PlaybackState != PlaybackState.Playing)
            {
                return;
            }

            if (PlaybackCursor == null || !string.Equals(PlaybackCursor.TrackId, track.Id, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.Equals(_audioEngine.LoadedTrackPath, track.Path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            StartTrailingSilenceAutoAdvanceTimer(track, startSeconds);
        }
    }

    private void StartTrailingSilenceAutoAdvanceTimer(MediaFile track, double startSeconds)
    {
        int? trailingSilenceMs = track.TrailingSilenceMs;
        if (!trailingSilenceMs.HasValue || trailingSilenceMs.Value < SkipSilenceMinimumDurationMs)
        {
            //_logger.LogDebug("SkipSilence: not scheduling (Reason=NoOrShortTrailingSilence, TrackId={TrackId}, TrailingMs={TrailingMs})", track.Id, trailingSilenceMs?.ToString() ?? "null");
            return;
        }

        // Do not schedule until the engine has loaded THIS track.
        if (!string.Equals(_audioEngine.LoadedTrackPath, track.Path, StringComparison.OrdinalIgnoreCase))
        {
            //_logger.LogDebug("SkipSilence: not scheduling (Reason=LoadedPathMismatch, TrackId={TrackId}, ExpectedPath={ExpectedPath}, LoadedPath={LoadedPath})", track.Id, track.Path, _audioEngine.LoadedTrackPath);
            return;
        }

        double engineTrackLengthSeconds = _audioEngine.CurrentTrackLength;
        if (engineTrackLengthSeconds <= 0)
        {
            //_logger.LogDebug("SkipSilence: not scheduling (Reason=EngineLengthUnavailable, TrackId={TrackId}, EngineLen={EngineLen})", track.Id, engineTrackLengthSeconds);
            return;
        }

        double metadataLengthSeconds = track.Duration;
        double trackLengthSeconds = Math.Max(engineTrackLengthSeconds, metadataLengthSeconds);

        double trailingSeconds = trailingSilenceMs.Value / 1000d;

        // Start crossfade at the start of trailing silence, but never later than FadeOut window before EOF.
        double fadeOutSeconds = Math.Max(0d, FadeOutMs / 1000d);
        double latestAllowedTriggerFromStart = Math.Max(0d, trackLengthSeconds - fadeOutSeconds);

        double triggerSecondsFromStart = Math.Max(0d, trackLengthSeconds - trailingSeconds);
        triggerSecondsFromStart = Math.Min(triggerSecondsFromStart, latestAllowedTriggerFromStart);

        // Schedule relative to CURRENT playback position.
        // The engine can temporarily report 0 even when we started at a non-zero offset (e.g., leading-silence skip).
        double enginePosSeconds = Math.Max(0, _audioEngine.CurrentTrackPosition);
        if (enginePosSeconds <= 0.001 && string.Equals(_currentTrackId, track.Id, StringComparison.Ordinal))
        {
            enginePosSeconds = Math.Max(0, _currentTrackStartSeconds);
        }

        double delaySeconds = Math.Max(0.01, triggerSecondsFromStart - enginePosSeconds);

        //_logger.LogDebug(
        //    "SkipSilence: trigger computed (TrackId={TrackId}, TrackLen={TrackLen}, TrailingSec={TrailingSec}, FadeOutSec={FadeOutSec}, TriggerFromStart={TriggerFromStart}, EnginePos={EnginePos}, DelaySec={DelaySec})",
        //    track.Id,
        //    trackLengthSeconds,
        //    trailingSeconds,
        //    fadeOutSeconds,
        //    triggerSecondsFromStart,
        //    enginePosSeconds,
        //    delaySeconds);

        if (PlaybackCursor == null)
        {
            //_logger.LogDebug("SkipSilence: not scheduling (Reason=CursorNull, TrackId={TrackId})", track.Id);
            return;
        }

        string trackId = track.Id;

        // Cancel any existing timer BEFORE creating the new state (otherwise we clear the freshly set state).
        StopTrailingSilenceTimer("reschedule", null);

        long timerVersion = Interlocked.Increment(ref _trailingSilenceTimerVersion);

        DateTimeOffset scheduleUtc = DateTimeOffset.UtcNow;
        DateTimeOffset dueUtc = scheduleUtc.AddSeconds(delaySeconds);

        _trailingSilenceTimerVersion = timerVersion;

        int dueMs = (int)Math.Clamp(delaySeconds * 1000d, 1d, (double)int.MaxValue);

        string playlistName = GetActivePlaybackSourcePlaylistName() ?? PlaybackCursor.PlaylistName;

        TrailingSilenceTimerState state = new TrailingSilenceTimerState
        {
            TimerVersion = timerVersion,
            TrackId = trackId,
            TrackPath = track.Path,
            PlaylistName = playlistName,
            TrackIndex = PlaybackCursor.TrackIndex,
            DueMs = dueMs
        };

        _trailingSilenceTimerState = state;

        //_logger.LogDebug(
        //    "SkipSilence: schedule trailing silence advance (TimerV={TimerV}, TrackId={TrackId}, DueMs={DueMs})",
        //    timerVersion,
        //    trackId,
        //    dueMs);

        _trailingSilenceTimer = new System.Threading.Timer(OnTrailingSilenceTimerFired, state, dueMs, System.Threading.Timeout.Infinite);
    }

    private void OnTrailingSilenceTimerFired(object? state)
    {
        TrailingSilenceTimerState? timerState = state as TrailingSilenceTimerState;
        long callbackTimerVersion = timerState?.TimerVersion ?? 0;

        DateTimeOffset callbackUtc = DateTimeOffset.UtcNow;

        lock (_sync)
        {
            string cursorSnapshot = PlaybackCursor == null ? "null" : $"{PlaybackCursor.PlaylistName}:{PlaybackCursor.TrackIndex}:{PlaybackCursor.TrackId}";

            //_logger.LogDebug(
            //    "SkipSilence: timer fired (TimerV={TimerV}, StateV={StateV}, ThreadId={ThreadId}, Cursor={Cursor}, PayloadCursor={PayloadCursor}, DueMs={DueMs})",
            //    callbackTimerVersion,
            //    Interlocked.Read(ref _trailingSilenceTimerVersion),
            //    Environment.CurrentManagedThreadId,
            //    cursorSnapshot,
            //    timerState == null ? "null" : $"{timerState.PlaylistName}:{timerState.TrackIndex}:{timerState.TrackId}",
            //    timerState?.DueMs);

            if (timerState == null)
            {
                return;
            }

            if (callbackTimerVersion != Interlocked.Read(ref _trailingSilenceTimerVersion))
            {
                return;
            }

            if (PlaybackState != PlaybackState.Playing)
            {
                return;
            }

            if (_crossfadeInProgress)
            {
                return;
            }

            string? activePlaylistName = GetActivePlaybackSourcePlaylistName();
            if (string.IsNullOrWhiteSpace(activePlaylistName))
            {
                return;
            }

            if (!string.Equals(activePlaylistName, timerState.PlaylistName, StringComparison.Ordinal)
                || !string.Equals(_currentTrackId, timerState.TrackId, StringComparison.Ordinal))
            {
                return;
            }

            StopTrailingSilenceTimer("fired", callbackTimerVersion);

            (string playlistName, IList<MediaFile>? tracks, int currentIndex) = ResolveActiveSourceTracks();
            if (tracks == null || tracks.Count == 0)
            {
                return;
            }

            bool shuffleMode = _settingsManager.Settings.ShuffleMode;
            int nextIndex = _trackNavigationService.GetNextTrackIndex(tracks, currentIndex, shuffleMode);
            if (nextIndex < 0 || nextIndex >= tracks.Count)
            {
                return;
            }

            MediaFile nextTrack = tracks[nextIndex];

            // Prefer crossfade when enabled.
            if (BeginCrossfadeTo(playlistName, nextIndex, nextTrack))
            {
                return;
            }

            PlayTrack(playlistName, nextIndex, nextTrack);
        }
    }

    private void StopTrailingSilenceTimer(string? reason = null, long? relatedTimerVersion = null)
    {
        // Invalidate any in-flight callbacks.
        Interlocked.Increment(ref _trailingSilenceTimerVersion);

        System.Threading.Timer? timerToDispose = _trailingSilenceTimer;
        if (timerToDispose != null)
        {
            try
            {
                //_logger.LogDebug(
                //    "SkipSilence: cancel trailing silence timer (Reason={Reason}, RelatedTimerV={RelatedTimerV}, Cursor={Cursor})",
                //    reason ?? "unknown",
                //    relatedTimerVersion?.ToString() ?? "null",
                //    PlaybackCursor == null ? "null" : $"{PlaybackCursor.PlaylistName}:{PlaybackCursor.TrackIndex}:{PlaybackCursor.TrackId}");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "SkipSilence: timer dispose threw (Reason={Reason})", reason ?? "unknown");
            }

            try
            {
                timerToDispose.Dispose();
            }
            catch
            {
            }

            _trailingSilenceTimer = null;
        }

        _trailingSilenceTimerState = null;
    }

    private void OnEnginePlaybackStopped()
    {
        lock (_sync)
        {
            StopEofWatchdog("engine-stopped");
            //_logger.LogDebug("OnEnginePlaybackStopped...");

            if (_suppressNextEngineStopped > 0)
            {
                _suppressNextEngineStopped--;
                return;
            }

            if (_crossfadeInProgress)
                return;

            StopTrailingSilenceTimer("engine-stopped", null);

            if (PlaybackState != PlaybackState.Stopped)
            {
                SetPlaybackState(PlaybackState.Stopped);
                // DO NOT set PlaybackCursor = null;
                _sharedDataModel.UpdateActiveTrack(null);
                WeakReferenceMessenger.Default.Send(new ActiveTrackChangedMessage(null));
            }
        }
    }

    private void OnEngineTrackEnded()
    {
        lock (_sync)
        {
            if (_crossfadeInProgress)
            {
                //_logger.LogDebug("OnEngineTrackEnded ignored: crossfade in progress");
                return;
            }

            StopTrailingSilenceTimer("engine-ended", null);

            if (PlaybackState != PlaybackState.Playing)
            {
                //_logger.LogDebug("OnEngineTrackEnded ignored: not playing (State={State})", PlaybackState);
                return;
            }

            (string playlistName, IList<MediaFile>? tracks, int currentIndex) = ResolveActiveSourceTracks();
            if (tracks == null || tracks.Count == 0)
            {
                return;
            }

            bool shuffleMode = _settingsManager.Settings.ShuffleMode;
            int nextIndex = _trackNavigationService.GetNextTrackIndex(tracks, currentIndex, shuffleMode);
            if (nextIndex < 0 || nextIndex >= tracks.Count)
            {
                return;
            }

            MediaFile nextTrack = tracks[nextIndex];
            if (BeginCrossfadeTo(playlistName, nextIndex, nextTrack))
            {
                return;
            }

            PlayTrack(playlistName, nextIndex, nextTrack);
        }
    }

    private void OnEngineCrossfadeCommitted(string committedPath)
    {
        lock (_sync)
        {
            if (!_crossfadeInProgress)
            {
                return;
            }

            long expectedVersion = _pendingCrossfadeVersion;
            if (expectedVersion == 0 || expectedVersion != _pendingCrossfadeVersion)
            {
                return;
            }

            if (_pendingCrossfadeTrack == null)
            {
                CancelCrossfade("commit-missing-track");
                return;
            }

            if (!string.Equals(_pendingCrossfadeTrack.Path, committedPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            MediaFile committedTrack = _pendingCrossfadeTrack;

            _crossfadeInProgress = false;
            _pendingCrossfadeTrack = null;
            _pendingCrossfadePlaylistName = null;
            _pendingCrossfadeTrackIndex = 0;
            _pendingCrossfadeVersion = 0;

            StartEofWatchdog("crossfade-commit");

            StopTrailingSilenceTimer("crossfade-commit", null);
            _ = Task.Run(() => StartTrailingSilenceAfterTrackLoadedAsync(committedTrack, 0));
        }
    }

    private bool BeginCrossfadeTo(string playlistName, int trackIndex, MediaFile track)
    {
        if (!IsCrossfadeEnabled)
        {
            return false;
        }

        if (_crossfadeInProgress)
        {
            return false;
        }

        if (PlaybackState != PlaybackState.Playing)
        {
            return false;
        }

        if (FadeInMs <= 0 || FadeOutMs <= 0)
        {
            return false;
        }

        StopTrailingSilenceTimer("crossfade-begin", null);

        bool began = _audioEngine.TryBeginCrossfade(track.Path, 0, FadeOutMs, FadeInMs, FadeCurveShape);
        if (!began)
        {
            return false;
        }

        _pendingCrossfadeVersion = Interlocked.Increment(ref _crossfadeVersion);
        _crossfadeInProgress = true;

        _pendingCrossfadePlaylistName = playlistName;
        _pendingCrossfadeTrackIndex = trackIndex;
        _pendingCrossfadeTrack = track;

        _activePlaybackSourcePlaylistName = playlistName;

        // Switch UI "now playing" immediately when the next track becomes audible.
        PlaybackCursor = new PlaybackCursor
        {
            PlaylistName = playlistName,
            TrackIndex = trackIndex,
            TrackId = track.Id
        };

        _sharedDataModel.UpdateActiveTrack(track);
        WeakReferenceMessenger.Default.Send(new ActiveTrackChangedMessage(track));

        _currentTrackId = track.Id;
        _currentTrackStartSeconds = 0;

        StartEofWatchdog("crossfade-begin");

        return true;
    }

    private void CancelCrossfade(string reason)
    {
        _crossfadeInProgress = false;
        _pendingCrossfadePlaylistName = null;
        _pendingCrossfadeTrackIndex = 0;
        _pendingCrossfadeTrack = null;
        _pendingCrossfadeVersion = 0;

        // Invalidate any in-flight commit callbacks.
        Interlocked.Increment(ref _crossfadeVersion);

        _logger.LogDebug("Crossfade cancelled (Reason={Reason})", reason);
    }

    public void Pause()
    {
        lock (_sync)
        {
            StopEofWatchdog("pause");
            _audioEngine.Pause();
            SetPlaybackState(PlaybackState.Paused);
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            _stopRequested = false;
            StartEofWatchdog("resume");

            _audioEngine.ResumePlay();
            SetPlaybackState(PlaybackState.Playing);
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _stopRequested = true;
            StopEofWatchdog("stop");
            CancelCrossfade("stop");
            StopTrailingSilenceTimer("stop", null);

            bool beganFadeOut = PlaybackState == PlaybackState.Playing
                && SmoothStopFadeEnabled
                && SmoothStopFadeMs > 0
                && _audioEngine.TryFadeOutAndStop(SmoothStopFadeMs, FadeCurveShape);

            if (!beganFadeOut)
            {
                StopCore();
                // DO NOT set PlaybackCursor = null;
                _sharedDataModel.UpdateActiveTrack(null);
                WeakReferenceMessenger.Default.Send(new ActiveTrackChangedMessage(null));
                WeakReferenceMessenger.Default.Send(new PlaybackStateChangedMessage(PlaybackState.Stopped));
            }
        }
    }

    public void Seek(double positionSeconds)
    {
        lock (_sync)
        {
            _audioEngine.SeekAudioFile(positionSeconds);

            if (PlaybackCursor != null)
            {
                _currentTrackId = PlaybackCursor.TrackId;
                _currentTrackStartSeconds = positionSeconds;
            }

            if (PlaybackState != PlaybackState.Playing || PlaybackCursor == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_audioEngine.LoadedTrackPath) && _audioEngine.CurrentTrackLength > 0)
            {
                List<MediaFile> tracks = _musicLibrary.GetTracksFromPlaylist(PlaybackCursor.PlaylistName);
                MediaFile? current = tracks.FirstOrDefault(t => string.Equals(t.Id, PlaybackCursor.TrackId, StringComparison.Ordinal));
                if (current != null)
                {
                    StopTrailingSilenceTimer("seek", null);
                    StartTrailingSilenceAutoAdvanceTimer(current, positionSeconds);
                }
            }
        }
    }

    public void Next()
    {
        lock (_sync)
        {
            (string playlistName, IList<MediaFile>? tracks, int currentIndex) = ResolveActiveSourceTracks();
            if (tracks == null || tracks.Count == 0)
            {
                _logger.LogDebug("Next ignored: no active source tracks");
                return;
            }

            if (_crossfadeInProgress)
                CancelCrossfade("next");

            StopTrailingSilenceTimer("next", null);
            StopEofWatchdog("next");

            bool shuffleMode = _settingsManager.Settings.ShuffleMode;
            int nextIndex = _trackNavigationService.GetNextTrackIndex(tracks, currentIndex, shuffleMode);
            if (nextIndex < 0 || nextIndex >= tracks.Count)
                return;

            MediaFile nextTrack = tracks[nextIndex];

            // Fast path when stopped or paused - no crossfade needed
            if (PlaybackState != PlaybackState.Playing)
            {
                PlayTrack(playlistName, nextIndex, nextTrack);
                return;
            }

            // Normal playing state - try crossfade
            if (BeginCrossfadeTo(playlistName, nextIndex, nextTrack))
                return;

            PlayTrack(playlistName, nextIndex, nextTrack);
        }
    }

    public void Prev()
    {
        lock (_sync)
        {
            (string playlistName, IList<MediaFile>? tracks, int currentIndex) = ResolveActiveSourceTracks();
            if (tracks == null || tracks.Count == 0)
            {
                _logger.LogDebug("Prev ignored: no active source tracks");
                return;
            }

            if (_crossfadeInProgress)
                CancelCrossfade("prev");

            StopTrailingSilenceTimer("prev", null);
            StopEofWatchdog("prev");

            bool shuffleMode = _settingsManager.Settings.ShuffleMode;
            int prevIndex = _trackNavigationService.GetPreviousTrackIndex(tracks, currentIndex, shuffleMode);
            if (prevIndex < 0 || prevIndex >= tracks.Count)
                return;

            MediaFile prevTrack = tracks[prevIndex];

            // Fast path when stopped or paused - no crossfade needed
            if (PlaybackState != PlaybackState.Playing)
            {
                PlayTrack(playlistName, prevIndex, prevTrack);
                return;
            }

            // Normal playing state - try crossfade
            if (BeginCrossfadeTo(playlistName, prevIndex, prevTrack))
                return;

            PlayTrack(playlistName, prevIndex, prevTrack);
        }
    }

    private (IList<MediaFile>? Tracks, int CurrentIndex) ResolvePlaylistTracks(string playlistName, int fallbackIndex)
    {
        List<MediaFile> tracks = playlistName == "Music Library"
            ? _musicLibrary.MainLibrary.ToList()
            : _musicLibrary.GetTracksFromPlaylist(playlistName);

        if (tracks.Count == 0)
        {
            return (null, fallbackIndex);
        }

        return (tracks, Math.Clamp(fallbackIndex, 0, Math.Max(0, tracks.Count - 1)));
    }

    private void StopCore()
    {
        _audioEngine.NextTrackPreStopVisuals();
        _audioEngine.Stop();
        // Do not mutate PlaybackState here; AudioEngine.Stop will raise OnPlaybackStopped.
        // Setting Stopped early breaks UI state (pause icon/seekbar) and can clear cursor.
    }

    private void StartEofWatchdog(string reason)
    {
        StopEofWatchdog($"restart:{reason}");

        //_logger.LogDebug("EOFWatchdog: start (Reason={Reason}, Cursor={Cursor})",
        //    reason,
        //    PlaybackCursor == null ? "null" : $"{PlaybackCursor.PlaylistName}:{PlaybackCursor.TrackIndex}:{PlaybackCursor.TrackId}");

        long version = Interlocked.Increment(ref _eofWatchdogVersion);

        _eofWatchdogTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                lock (_sync)
                {
                    if (_stopRequested)
                    {
                        return;
                    }

                    if (version != Interlocked.Read(ref _eofWatchdogVersion))
                    {
                        return;
                    }

                    if (PlaybackState != PlaybackState.Playing)
                    {
                        return;
                    }

                    if (_crossfadeInProgress)
                    {
                        return;
                    }

                    // If trailing-silence timer is armed, it owns the transition timing.
                    if (_trailingSilenceTimer != null)
                    {
                        return;
                    }

                    (string playlistName, IList<MediaFile>? tracks, int currentIndex) = ResolveActiveSourceTracks();
                    if (tracks == null || tracks.Count == 0)
                    {
                        return;
                    }

                    double len = _audioEngine.CurrentTrackLength;
                    double pos = _audioEngine.CurrentTrackPosition;
                    if (len <= 0 || double.IsNaN(len) || double.IsNaN(pos))
                    {
                        return;
                    }

                    double fadeOutSeconds = Math.Max(0d, FadeOutMs / 1000d);
                    double thresholdSeconds = Math.Max(0.25d, fadeOutSeconds + 0.10d);
                    double remainingSeconds = len - pos;

                    if (remainingSeconds > thresholdSeconds)
                    {
                        return;
                    }

                    //_logger.LogDebug("EOFWatchdog: near EOF (Len={Len}, Pos={Pos}, Remaining={Remaining}, Threshold={Threshold}, FadeOutSec={FadeOutSec}, Cursor={Cursor})",
                    //    len,
                    //    pos,
                    //    remainingSeconds,
                    //    thresholdSeconds,
                    //    fadeOutSeconds,
                    //    $"{PlaybackCursor.PlaylistName}:{PlaybackCursor.TrackIndex}:{PlaybackCursor.TrackId}");

                    // Reuse the standard advance logic.
                    _logger.LogInformation("EOFWatchdog: Resolving next track from {PlaylistName} (currentIndex={CurrentIndex}, trackCount={TrackCount})", 
                        playlistName, currentIndex, tracks.Count);

                    bool shuffleMode = _settingsManager.Settings.ShuffleMode;
                    int nextIndex = _trackNavigationService.GetNextTrackIndex(tracks, currentIndex, shuffleMode);
                    if (nextIndex < 0 || nextIndex >= tracks.Count)
                    {
                        _logger.LogInformation("EOFWatchdog: No next track available (nextIndex={NextIndex})", nextIndex);
                        return;
                    }

                    MediaFile nextTrack = tracks[nextIndex];
                    _logger.LogInformation("EOFWatchdog: Next track is {TrackTitle} ({TrackId}) at index {NextIndex}", nextTrack.Title, nextTrack.Id, nextIndex);

                    // Prevent repeated triggers for the same EOF window.
                    StopEofWatchdog("triggered");

                    if (BeginCrossfadeTo(playlistName, nextIndex, nextTrack))
                    {
                        return;
                    }

                    _logger.LogInformation("EOFWatchdog: Calling PlayTrack with PlaylistName={PlaylistName}", playlistName);
                    PlayTrack(playlistName, nextIndex, nextTrack);
                }
            }
            catch
            {
            }
        }, null, 250, 250);
    }

    private void StopEofWatchdog(string reason)
    {
        Interlocked.Increment(ref _eofWatchdogVersion);

        System.Threading.Timer? timerToDispose = _eofWatchdogTimer;
        if (timerToDispose != null)
        {
            //try
            //{
            //    _logger.LogDebug("EOFWatchdog: stop (Reason={Reason})", reason);
            //}
            //catch
            //{
            //}

            try
            {
                timerToDispose.Dispose();
            }
            catch
            {
            }
            _eofWatchdogTimer = null;
        }
    }

    public void Receive(ShuffleModeMessage message)
    {
        lock (_sync)
        {
            bool enabled = message.Value;

            if (!enabled)
            {
                //_logger.LogDebug("ShuffleMode: disabled -> clearing shuffle list");
                _trackNavigationService.ClearShuffle();
                return;
            }

            if (PlaybackCursor == null)
            {
                //_logger.LogDebug("ShuffleMode: enabled but no PlaybackCursor; shuffle will initialize on first Next/Prev");
                _trackNavigationService.ClearShuffle();
                return;
            }

            List<MediaFile> tracks = _musicLibrary.GetTracksFromPlaylist(PlaybackCursor.PlaylistName);
            if (tracks.Count == 0)
            {
                //_logger.LogDebug("ShuffleMode: enabled but playlist '{PlaylistName}' is empty", PlaybackCursor.PlaylistName);
                _trackNavigationService.ClearShuffle();
                return;
            }

            //_logger.LogDebug("ShuffleMode: enabled -> initializing shuffle list (PlaylistName={PlaylistName}, TrackCount={TrackCount}, CurrentTrackId={TrackId})",
            //    PlaybackCursor.PlaylistName,
            //    tracks.Count,
            //    PlaybackCursor.TrackId);

            _trackNavigationService.InitializeShuffle(tracks, PlaybackCursor.TrackId);
        }
    }
}
