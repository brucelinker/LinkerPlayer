using LinkerPlayer.Messages;
using LinkerPlayer.Audio;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.Services.Playback;
using LinkerPlayer.Tests.Fakes;
using LinkerPlayer.ViewModels;
using ManagedBass;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;

namespace LinkerPlayer.Tests.Services;

public class PlaybackCoordinatorTests
{
    [StaFact]
    public void PlayTrack_SetsPlaybackStateAndCursor()
    {
        FakeAudioEngine audioEngine = new FakeAudioEngine
        {
            CurrentTrackLength = 120
        };

        Mock<ITrackNavigationService> nav = new Mock<ITrackNavigationService>();
        FakeMusicLibrary musicLibrary = new FakeMusicLibrary();
        FakeSettingsManager settingsManager = new FakeSettingsManager
        {
            Settings = new AppSettings { CrossfadeEnabled = false }
        };

        SharedDataModel shared = new SharedDataModel();
        ILogger<PlaybackCoordinator> logger = Mock.Of<ILogger<PlaybackCoordinator>>();

        PlaybackCoordinator coordinator = new PlaybackCoordinator(
            audioEngine,
            nav.Object,
            musicLibrary,
            settingsManager,
            shared,
            logger);

        MediaFile track = new MediaFile
        {
            Id = "t1",
            Path = "c:/music/t1.mp3",
            Title = "T1",
            Duration = 120
        };

        coordinator.PlayTrack("P1", 3, track, 0);

        coordinator.PlaybackState.ShouldBe(PlaybackState.Playing);
        coordinator.PlaybackCursor.ShouldNotBeNull();
        coordinator.PlaybackCursor!.PlaylistName.ShouldBe("P1");
        coordinator.PlaybackCursor.TrackIndex.ShouldBe(3);
        coordinator.PlaybackCursor.TrackId.ShouldBe("t1");
    }

    [StaFact]
    public void Stop_WhenSmoothStopFadeEnabled_UsesFadeOutAndStop()
    {
        FakeAudioEngine audioEngine = new FakeAudioEngine
        {
            CurrentTrackLength = 120,
            FadeOutAndStopSupported = true
        };

        Mock<ITrackNavigationService> nav = new Mock<ITrackNavigationService>();
        FakeMusicLibrary musicLibrary = new FakeMusicLibrary();
        FakeSettingsManager settingsManager = new FakeSettingsManager
        {
            Settings = new AppSettings
            {
                SmoothStopFadeEnabled = true,
                SmoothStopFadeMs = 150,
                CrossfadeCurveShape = FadeCurveShape.Cosine
            }
        };

        SharedDataModel shared = new SharedDataModel();
        ILogger<PlaybackCoordinator> logger = Mock.Of<ILogger<PlaybackCoordinator>>();

        PlaybackCoordinator coordinator = new PlaybackCoordinator(
            audioEngine,
            nav.Object,
            musicLibrary,
            settingsManager,
            shared,
            logger);

        MediaFile track = new MediaFile { Id = "t1", Path = "c:/music/t1.mp3", Duration = 120 };
        coordinator.PlayTrack("P1", 0, track, 0);

        coordinator.Stop();

        audioEngine.LastFadeOutAndStopMs.ShouldBe(150);
        audioEngine.LastFadeOutAndStopCurveShape.ShouldBe(FadeCurveShape.Cosine);
    }

    [StaFact]
    public void Next_WhenCrossfadeEnabled_BeginsCrossfade()
    {
        FakeAudioEngine audioEngine = new FakeAudioEngine
        {
            CurrentTrackLength = 120,
            CrossfadeSupported = true
        };

        TrackNavigationService navService = new TrackNavigationService(Mock.Of<ILogger<TrackNavigationService>>());

        FakeMusicLibrary musicLibrary = new FakeMusicLibrary();
        List<MediaFile> tracks = new List<MediaFile>
        {
            new MediaFile { Id = "t1", Path = "c:/music/t1.mp3", Duration = 120 },
            new MediaFile { Id = "t2", Path = "c:/music/t2.mp3", Duration = 180 }
        };
        musicLibrary.GetTracksFromPlaylistFunc = _ => tracks;

        FakeSettingsManager settingsManager = new FakeSettingsManager
        {
            Settings = new AppSettings
            {
                CrossfadeEnabled = true,
                CrossfadeFadeInMs = 200,
                CrossfadeFadeOutMs = 200,
                CrossfadeCurveShape = FadeCurveShape.Cosine
            }
        };

        SharedDataModel shared = new SharedDataModel();
        ILogger<PlaybackCoordinator> logger = Mock.Of<ILogger<PlaybackCoordinator>>();

        PlaybackCoordinator coordinator = new PlaybackCoordinator(
            audioEngine,
            navService,
            musicLibrary,
            settingsManager,
            shared,
            logger);

        coordinator.PlayTrack("P1", 0, tracks[0], 0);

        coordinator.Next();

        audioEngine.LastCrossfadeNextPath.ShouldBe("c:/music/t2.mp3");
        audioEngine.LastCrossfadeFadeInMs.ShouldBe(200);
        audioEngine.LastCrossfadeFadeOutMs.ShouldBe(200);
        audioEngine.LastCrossfadeCurveShape.ShouldBe(FadeCurveShape.Cosine);
    }

    [StaFact]
    public void CrossfadeCommitted_UpdatesCursorAndActiveTrack()
    {
        FakeAudioEngine audioEngine = new FakeAudioEngine
        {
            CurrentTrackLength = 120,
            CrossfadeSupported = true
        };

        TrackNavigationService navService = new TrackNavigationService(Mock.Of<ILogger<TrackNavigationService>>());

        FakeMusicLibrary musicLibrary = new FakeMusicLibrary();
        List<MediaFile> tracks = new List<MediaFile>
        {
            new MediaFile { Id = "t1", Path = "c:/music/t1.mp3", Duration = 120 },
            new MediaFile { Id = "t2", Path = "c:/music/t2.mp3", Duration = 180 }
        };
        musicLibrary.GetTracksFromPlaylistFunc = _ => tracks;

        FakeSettingsManager settingsManager = new FakeSettingsManager
        {
            Settings = new AppSettings
            {
                CrossfadeEnabled = true,
                CrossfadeFadeInMs = 200,
                CrossfadeFadeOutMs = 200,
                CrossfadeCurveShape = FadeCurveShape.Cosine
            }
        };

        SharedDataModel shared = new SharedDataModel();
        ILogger<PlaybackCoordinator> logger = Mock.Of<ILogger<PlaybackCoordinator>>();

        PlaybackCoordinator coordinator = new PlaybackCoordinator(
            audioEngine,
            navService,
            musicLibrary,
            settingsManager,
            shared,
            logger);

        MediaFile? lastActiveTrack = null;
        WeakReferenceMessenger.Default.Register<ActiveTrackChangedMessage>(this, (_, m) => lastActiveTrack = m.Value);

        coordinator.PlayTrack("P1", 0, tracks[0], 0);
        coordinator.Next();

        audioEngine.RaiseCrossfadeCommitted("c:/music/t2.mp3");

        coordinator.PlaybackCursor.ShouldNotBeNull();
        coordinator.PlaybackCursor!.TrackId.ShouldBe("t2");
        coordinator.PlaybackCursor.TrackIndex.ShouldBe(1);
        lastActiveTrack.ShouldNotBeNull();
        lastActiveTrack!.Id.ShouldBe("t2");
    }
}
