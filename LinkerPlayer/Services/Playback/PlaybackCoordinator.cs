using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.Messages;
using LinkerPlayer.ViewModels;
using Microsoft.Extensions.Logging;
using ManagedBass;
using CommunityToolkit.Mvvm.Messaging;
using System.Diagnostics;
using System.Threading;

namespace LinkerPlayer.Services.Playback;

public sealed class PlaybackCoordinator : IPlaybackCoordinator, IRecipient<ShuffleModeMessage>
{
    private readonly IAudioEngine _audioEngine;
    private readonly ITrackNavigationService _trackNavigationService;
    private readonly IMusicLibrary _musicLibrary;
    private readonly ISettingsManager _settingsManager;
    private readonly ISharedDataModel _sharedDataModel;
    private readonly ILogger<PlaybackCoordinator> _logger;

    private readonly object _sync = new object();

    private string? _userSelectedPlaylistName;
    private int _userSelectedTrackIndex;
    private MediaFile? _userSelectedTrack;

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

    public void SetUserSelection(string playlistName, int trackIndex, MediaFile track)
    {
        if (playlistName == null)
        {
            throw new ArgumentNullException(nameof(playlistName));
        }

        if (track == null)
        {
            throw new ArgumentNullException(nameof(track));
        }

        lock (_sync)
        {
            _userSelectedPlaylistName = playlistName;
            _userSelectedTrackIndex = trackIndex;
            _userSelectedTrack = track;
        }
    }

    public void PlaySelected()
    {
        lock (_sync)
        {
            if (_userSelectedTrack == null || string.IsNullOrWhiteSpace(_userSelectedPlaylistName))
            {
                _logger.LogWarning("PlaySelected ignored: missing selection (PlaylistName={PlaylistName}, Track={Track})",
                    _userSelectedPlaylistName ?? "null",
                    _userSelectedTrack?.Id ?? "null");
                return;
            }

            PlayTrack(_userSelectedPlaylistName, _userSelectedTrackIndex, _userSelectedTrack);
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

        _logger.LogInformation("PlayTrack (Playlist={Playlist}, Index={Index}, TrackId={TrackId})", playlistName, trackIndex, track.Id);
        _logger.LogDebug("PlayTrack analysis (TrackId={TrackId}, LeadingMs={LeadingMs}, TrailingMs={TrailingMs})", track.Id, track.LeadingSilenceMs, track.TrailingSilenceMs);

        if (string.IsNullOrWhiteSpace(playlistName))
        {
            _logger.LogWarning("PlayTrack called with empty playlistName for track '{TrackTitle}' ({TrackId}). Navigation will not work.", track.Title, track.Id);
        }

        lock (_sync)
        {
            _stopRequested = false;
            StartEofWatchdog("playtrack");

            StopTrailingSilenceTimer("playtrack", null);

            // StopCore + AudioEngine.Stop can result in multiple OnPlaybackStopped notifications.
            _suppressNextEngineStopped += 2;
            StopCore();

            PlaybackCursor = new PlaybackCursor { PlaylistName = playlistName, TrackIndex = trackIndex, TrackId = track.Id };

            _sharedDataModel.UpdateActiveTrack(track);
            WeakReferenceMessenger.Default.Send(new ActiveTrackChangedMessage(track));

            _audioEngine.PathToMusic = track.Path;

            double startSeconds = positionSeconds;
            int? leadingSilenceMs = track.LeadingSilenceMs;
            if (leadingSilenceMs.HasValue && leadingSilenceMs.Value >= SkipSilenceMinimumDurationMs)
            {
                double leadingSeconds = leadingSilenceMs.Value / 1000d;
                startSeconds = Math.Max(startSeconds, leadingSeconds);
                _logger.LogDebug("SkipSilence: applying leading silence seek (TrackId={TrackId}, LeadingMs={LeadingMs}, StartSeconds={StartSeconds})", track.Id, leadingSilenceMs.Value, startSeconds);
            }

            _currentTrackId = track.Id;
            _currentTrackStartSeconds = startSeconds;

            _ = Task.Run(() => _audioEngine.Play(track.Path, startSeconds));
            _ = Task.Run(() => StartTrailingSilenceAfterTrackLoadedAsync(track, startSeconds));

            PlaybackState = PlaybackState.Playing;
            WeakReferenceMessenger.Default.Send(new PlaybackStateChangedMessage(PlaybackState));
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
            _logger.LogDebug("SkipSilence: not scheduling (Reason=NoOrShortTrailingSilence, TrackId={TrackId}, TrailingMs={TrailingMs})", track.Id, trailingSilenceMs?.ToString() ?? "null");
            return;
        }

        // Do not schedule until the engine has loaded THIS track.
        if (!string.Equals(_audioEngine.LoadedTrackPath, track.Path, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("SkipSilence: not scheduling (Reason=LoadedPathMismatch, TrackId={TrackId}, ExpectedPath={ExpectedPath}, LoadedPath={LoadedPath})", track.Id, track.Path, _audioEngine.LoadedTrackPath);
            return;
        }

        double engineTrackLengthSeconds = _audioEngine.CurrentTrackLength;
        if (engineTrackLengthSeconds <= 0)
        {
            _logger.LogDebug("SkipSilence: not scheduling (Reason=EngineLengthUnavailable, TrackId={TrackId}, EngineLen={EngineLen})", track.Id, engineTrackLengthSeconds);
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

        _logger.LogDebug(
            "SkipSilence: trigger computed (TrackId={TrackId}, TrackLen={TrackLen}, TrailingSec={TrailingSec}, FadeOutSec={FadeOutSec}, TriggerFromStart={TriggerFromStart}, EnginePos={EnginePos}, DelaySec={DelaySec})",
            track.Id,
            trackLengthSeconds,
            trailingSeconds,
            fadeOutSeconds,
            triggerSecondsFromStart,
            enginePosSeconds,
            delaySeconds);

        if (PlaybackCursor == null)
        {
            _logger.LogDebug("SkipSilence: not scheduling (Reason=CursorNull, TrackId={TrackId})", track.Id);
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

        TrailingSilenceTimerState state = new TrailingSilenceTimerState
        {
            TimerVersion = timerVersion,
            TrackId = trackId,
            TrackPath = track.Path,
            PlaylistName = PlaybackCursor.PlaylistName,
            TrackIndex = PlaybackCursor.TrackIndex,
            DueMs = dueMs
        };

        _trailingSilenceTimerState = state;

        _logger.LogDebug(
            "SkipSilence: schedule trailing silence advance (TimerV={TimerV}, TrackId={TrackId}, DueMs={DueMs})",
            timerVersion,
            trackId,
            dueMs);

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

            _logger.LogDebug(
                "SkipSilence: timer fired (TimerV={TimerV}, StateV={StateV}, ThreadId={ThreadId}, Cursor={Cursor}, PayloadCursor={PayloadCursor}, DueMs={DueMs})",
                callbackTimerVersion,
                Interlocked.Read(ref _trailingSilenceTimerVersion),
                Environment.CurrentManagedThreadId,
                cursorSnapshot,
                timerState == null ? "null" : $"{timerState.PlaylistName}:{timerState.TrackIndex}:{timerState.TrackId}",
                timerState?.DueMs);

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

            if (PlaybackCursor == null)
            {
                return;
            }

            if (!string.Equals(PlaybackCursor.PlaylistName, timerState.PlaylistName, StringComparison.Ordinal)
                || PlaybackCursor.TrackIndex != timerState.TrackIndex
                || !string.Equals(PlaybackCursor.TrackId, timerState.TrackId, StringComparison.Ordinal))
            {
                return;
            }

            StopTrailingSilenceTimer("fired", callbackTimerVersion);

            (IList<MediaFile>? tracks, int currentIndex) = ResolvePlaylistTracks(PlaybackCursor.PlaylistName, PlaybackCursor.TrackIndex);
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
            if (BeginCrossfadeTo(PlaybackCursor.PlaylistName, nextIndex, nextTrack))
            {
                return;
            }

            PlayTrack(PlaybackCursor.PlaylistName, nextIndex, nextTrack);
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
                _logger.LogDebug(
                    "SkipSilence: cancel trailing silence timer (Reason={Reason}, RelatedTimerV={RelatedTimerV}, Cursor={Cursor})",
                    reason ?? "unknown",
                    relatedTimerVersion?.ToString() ?? "null",
                    PlaybackCursor == null ? "null" : $"{PlaybackCursor.PlaylistName}:{PlaybackCursor.TrackIndex}:{PlaybackCursor.TrackId}");
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
            _logger.LogDebug("OnEnginePlaybackStopped (Suppressed={Suppressed}, State={State}, Cursor={Cursor})",
                _suppressNextEngineStopped,
                PlaybackState,
                PlaybackCursor == null ? "null" : $"{PlaybackCursor.PlaylistName}:{PlaybackCursor.TrackIndex}:{PlaybackCursor.TrackId}");

            if (_suppressNextEngineStopped > 0)
            {
                _suppressNextEngineStopped--;
                _logger.LogDebug("OnEnginePlaybackStopped suppressed (Remaining={Remaining})", _suppressNextEngineStopped);
                return;
            }

            if (_crossfadeInProgress)
            {
                _logger.LogDebug("OnEnginePlaybackStopped ignored: crossfade in progress");
                return;
            }

            // Only now consider playback truly stopped; safe to clear the timer.
            StopTrailingSilenceTimer("engine-stopped", null);

            if (PlaybackState != PlaybackState.Stopped)
            {
                PlaybackState = PlaybackState.Stopped;
                PlaybackCursor = null;
                _currentTrackId = null;
                _currentTrackStartSeconds = 0;
                _sharedDataModel.UpdateActiveTrack(null);
                WeakReferenceMessenger.Default.Send(new ActiveTrackChangedMessage(null));
                WeakReferenceMessenger.Default.Send(new PlaybackStateChangedMessage(PlaybackState));
            }
        }
    }

    private void OnEngineTrackEnded()
    {
        lock (_sync)
        {
            if (_crossfadeInProgress)
            {
                _logger.LogDebug("OnEngineTrackEnded ignored: crossfade in progress");
                return;
            }

            StopTrailingSilenceTimer("engine-ended", null);

            if (PlaybackState != PlaybackState.Playing)
            {
                _logger.LogDebug("OnEngineTrackEnded ignored: not playing (State={State})", PlaybackState);
                return;
            }

            if (PlaybackCursor == null)
            {
                _logger.LogDebug("OnEngineTrackEnded ignored: cursor is null");
                return;
            }

            (IList<MediaFile>? tracks, int currentIndex) = ResolvePlaylistTracks(PlaybackCursor.PlaylistName, PlaybackCursor.TrackIndex);
            if (tracks == null || tracks.Count == 0)
            {
                _logger.LogDebug("OnEngineTrackEnded ignored: no tracks resolved (PlaylistName={PlaylistName})", PlaybackCursor.PlaylistName);
                return;
            }

            bool shuffleMode = _settingsManager.Settings.ShuffleMode;
            int nextIndex = _trackNavigationService.GetNextTrackIndex(tracks, currentIndex, shuffleMode);
            if (nextIndex < 0 || nextIndex >= tracks.Count)
            {
                _logger.LogDebug("OnEngineTrackEnded ignored: computed nextIndex {NextIndex} out of range 0..{MaxIndex} for playlist '{PlaylistName}'", nextIndex, tracks.Count - 1, PlaybackCursor.PlaylistName);
                return;
            }

            MediaFile nextTrack = tracks[nextIndex];
            if (BeginCrossfadeTo(PlaybackCursor.PlaylistName, nextIndex, nextTrack))
            {
                return;
            }

            PlayTrack(PlaybackCursor.PlaylistName, nextIndex, nextTrack);
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
            PlaybackState = PlaybackState.Paused;
            WeakReferenceMessenger.Default.Send(new PlaybackStateChangedMessage(PlaybackState));
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            _stopRequested = false;
            StartEofWatchdog("resume");

            _audioEngine.ResumePlay();
            PlaybackState = PlaybackState.Playing;
            WeakReferenceMessenger.Default.Send(new PlaybackStateChangedMessage(PlaybackState));
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

            if (beganFadeOut)
            {
                return;
            }

            StopCore();
            PlaybackCursor = null;
            _sharedDataModel.UpdateActiveTrack(null);
            WeakReferenceMessenger.Default.Send(new ActiveTrackChangedMessage(null));

            WeakReferenceMessenger.Default.Send(new PlaybackStateChangedMessage(PlaybackState.Stopped));
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
            _logger.LogInformation("Next (Cursor={Cursor})", PlaybackCursor == null ? "null" : $"{PlaybackCursor.PlaylistName}:{PlaybackCursor.TrackIndex}:{PlaybackCursor.TrackId}");
            if (PlaybackCursor == null)
            {
                _logger.LogDebug("Next ignored: PlaybackCursor is null");
                return;
            }

            // User intent: Next should always win. If we're mid-crossfade, cancel it and start a new transition.
            if (_crossfadeInProgress)
            {
                CancelCrossfade("next");
            }

            StopTrailingSilenceTimer("next", null);
            StartEofWatchdog("next");

            (IList<MediaFile>? tracks, int currentIndex) = ResolvePlaylistTracks(PlaybackCursor.PlaylistName, PlaybackCursor.TrackIndex);
            if (tracks == null || tracks.Count == 0)
            {
                _logger.LogDebug("Next ignored: no tracks resolved for playlist '{PlaylistName}'", PlaybackCursor.PlaylistName);
                return;
            }

            bool shuffleMode = _settingsManager.Settings.ShuffleMode;

            int nextIndex = _trackNavigationService.GetNextTrackIndex(tracks, currentIndex, shuffleMode);
            if (nextIndex < 0 || nextIndex >= tracks.Count)
            {
                _logger.LogDebug("Next ignored: computed nextIndex {NextIndex} out of range 0..{MaxIndex} for playlist '{PlaylistName}'", nextIndex, tracks.Count - 1, PlaybackCursor.PlaylistName);
                return;
            }

            MediaFile nextTrack = tracks[nextIndex];

            if (BeginCrossfadeTo(PlaybackCursor.PlaylistName, nextIndex, nextTrack))
            {
                return;
            }

            PlayTrack(PlaybackCursor.PlaylistName, nextIndex, nextTrack);
        }
    }

    public void Prev()
    {
        lock (_sync)
        {
            _logger.LogInformation("Prev (Cursor={Cursor})", PlaybackCursor == null ? "null" : $"{PlaybackCursor.PlaylistName}:{PlaybackCursor.TrackIndex}:{PlaybackCursor.TrackId}");
            if (PlaybackCursor == null)
            {
                _logger.LogDebug("Prev ignored: PlaybackCursor is null");
                return;
            }

            // User intent: Prev should always win. If we're mid-crossfade, cancel it and start a new transition.
            if (_crossfadeInProgress)
            {
                CancelCrossfade("prev");
            }

            StopTrailingSilenceTimer("prev", null);
            StartEofWatchdog("prev");

            (IList<MediaFile>? tracks, int currentIndex) = ResolvePlaylistTracks(PlaybackCursor.PlaylistName, PlaybackCursor.TrackIndex);
            if (tracks == null || tracks.Count == 0)
            {
                _logger.LogDebug("Prev ignored: no tracks resolved for playlist '{PlaylistName}'", PlaybackCursor.PlaylistName);
                return;
            }

            bool shuffleMode = _settingsManager.Settings.ShuffleMode;

            int previousIndex = _trackNavigationService.GetPreviousTrackIndex(tracks, currentIndex, shuffleMode);
            if (previousIndex < 0 || previousIndex >= tracks.Count)
            {
                _logger.LogDebug("Prev ignored: computed previousIndex {PreviousIndex} out of range 0..{MaxIndex} for playlist '{PlaylistName}'", previousIndex, tracks.Count - 1, PlaybackCursor.PlaylistName);
                return;
            }

            MediaFile previousTrack = tracks[previousIndex];

            if (BeginCrossfadeTo(PlaybackCursor.PlaylistName, previousIndex, previousTrack))
            {
                return;
            }

            PlayTrack(PlaybackCursor.PlaylistName, previousIndex, previousTrack);
        }
    }

    private (IList<MediaFile>? Tracks, int CurrentIndex) ResolvePlaylistTracks(string playlistName, int fallbackIndex)
    {
        List<MediaFile> tracks = _musicLibrary.GetTracksFromPlaylist(playlistName);
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

        _logger.LogDebug("EOFWatchdog: start (Reason={Reason}, Cursor={Cursor})",
            reason,
            PlaybackCursor == null ? "null" : $"{PlaybackCursor.PlaylistName}:{PlaybackCursor.TrackIndex}:{PlaybackCursor.TrackId}");

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

                    if (PlaybackCursor == null)
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

                    _logger.LogDebug("EOFWatchdog: near EOF (Len={Len}, Pos={Pos}, Remaining={Remaining}, Threshold={Threshold}, FadeOutSec={FadeOutSec}, Cursor={Cursor})",
                        len,
                        pos,
                        remainingSeconds,
                        thresholdSeconds,
                        fadeOutSeconds,
                        $"{PlaybackCursor.PlaylistName}:{PlaybackCursor.TrackIndex}:{PlaybackCursor.TrackId}");

                    // Reuse the standard advance logic.
                    (IList<MediaFile>? tracks, int currentIndex) = ResolvePlaylistTracks(PlaybackCursor.PlaylistName, PlaybackCursor.TrackIndex);
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

                    // Prevent repeated triggers for the same EOF window.
                    StopEofWatchdog("triggered");

                    if (BeginCrossfadeTo(PlaybackCursor.PlaylistName, nextIndex, nextTrack))
                    {
                        return;
                    }

                    PlayTrack(PlaybackCursor.PlaylistName, nextIndex, nextTrack);
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
            try
            {
                _logger.LogDebug("EOFWatchdog: stop (Reason={Reason})", reason);
            }
            catch
            {
            }

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
                _logger.LogDebug("ShuffleMode: disabled -> clearing shuffle list");
                _trackNavigationService.ClearShuffle();
                return;
            }

            if (PlaybackCursor == null)
            {
                _logger.LogDebug("ShuffleMode: enabled but no PlaybackCursor; shuffle will initialize on first Next/Prev");
                _trackNavigationService.ClearShuffle();
                return;
            }

            List<MediaFile> tracks = _musicLibrary.GetTracksFromPlaylist(PlaybackCursor.PlaylistName);
            if (tracks.Count == 0)
            {
                _logger.LogDebug("ShuffleMode: enabled but playlist '{PlaylistName}' is empty", PlaybackCursor.PlaylistName);
                _trackNavigationService.ClearShuffle();
                return;
            }

            _logger.LogDebug("ShuffleMode: enabled -> initializing shuffle list (PlaylistName={PlaylistName}, TrackCount={TrackCount}, CurrentTrackId={TrackId})",
                PlaybackCursor.PlaylistName,
                tracks.Count,
                PlaybackCursor.TrackId);

            _trackNavigationService.InitializeShuffle(tracks, PlaybackCursor.TrackId);
        }
    }
}
