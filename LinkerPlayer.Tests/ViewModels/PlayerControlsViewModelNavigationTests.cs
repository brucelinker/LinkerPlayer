using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.Services.Playback;
using LinkerPlayer.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;

namespace LinkerPlayer.Tests.ViewModels;

public class PlayerControlsViewModelNavigationTests
{
    [StaFact]
    public void NextTrack_DoesNotStopPlaybackCoordinatorFirst()
    {
        Mock<IAudioEngine> audioEngine = new Mock<IAudioEngine>();
        audioEngine.SetupGet(a => a.IsPlaying).Returns(true);

        PlaylistTabsViewModel playlistTabsVm = CreatePlaylistTabsVm();

        Mock<IPlaybackCoordinator> coordinator = new Mock<IPlaybackCoordinator>();
        Mock<ISettingsManager> settings = new Mock<ISettingsManager>();
        settings.SetupGet(s => s.Settings).Returns(new AppSettings());

        ISharedDataModel shared = new SharedDataModel();
        ILogger<PlayerControlsViewModel> logger = Mock.Of<ILogger<PlayerControlsViewModel>>();

        PlayerControlsViewModel vm = new PlayerControlsViewModel(
            audioEngine.Object,
            playlistTabsVm,
            coordinator.Object,
            settings.Object,
            shared,
            logger);

        vm.NextTrack();

        Mock.Get(coordinator.Object).Verify(c => c.Stop(), Times.Never);
        Mock.Get(coordinator.Object).Verify(c => c.Next(), Times.Once);
    }

    [StaFact]
    public void PreviousTrack_DoesNotStopPlaybackCoordinatorFirst()
    {
        Mock<IAudioEngine> audioEngine = new Mock<IAudioEngine>();
        audioEngine.SetupGet(a => a.IsPlaying).Returns(true);

        PlaylistTabsViewModel playlistTabsVm = CreatePlaylistTabsVm();

        Mock<IPlaybackCoordinator> coordinator = new Mock<IPlaybackCoordinator>();
        Mock<ISettingsManager> settings = new Mock<ISettingsManager>();
        settings.SetupGet(s => s.Settings).Returns(new AppSettings());

        ISharedDataModel shared = new SharedDataModel();
        ILogger<PlayerControlsViewModel> logger = Mock.Of<ILogger<PlayerControlsViewModel>>();

        PlayerControlsViewModel vm = new PlayerControlsViewModel(
            audioEngine.Object,
            playlistTabsVm,
            coordinator.Object,
            settings.Object,
            shared,
            logger);

        vm.PreviousTrack();

        Mock.Get(coordinator.Object).Verify(c => c.Stop(), Times.Never);
        Mock.Get(coordinator.Object).Verify(c => c.Prev(), Times.Once);
    }

    private static PlaylistTabsViewModel CreatePlaylistTabsVm()
    {
        Mock<IMusicLibrary> musicLibrary = new Mock<IMusicLibrary>();
        musicLibrary.SetupGet(m => m.Playlists).Returns(new System.Collections.ObjectModel.ObservableCollection<Playlist>
        {
            new Playlist { Name = "P1", TrackIds = new System.Collections.ObjectModel.ObservableCollection<string>() }
        });
        musicLibrary.SetupGet(m => m.MainLibrary).Returns(new System.Collections.ObjectModel.ObservableCollection<MediaFile>());
        musicLibrary.Setup(m => m.GetPlaylists()).Returns(musicLibrary.Object.Playlists.ToList());

        Mock<ISharedDataModel> shared = new Mock<ISharedDataModel>();
        shared.SetupGet(s => s.ActiveTrack).Returns((MediaFile?)null);
        shared.SetupGet(s => s.SelectedTrackIndex).Returns(-1);
        shared.SetupGet(s => s.SelectedTrack).Returns((MediaFile?)null);

        Mock<ISettingsManager> settingsManager = new Mock<ISettingsManager>();
        settingsManager.SetupGet(s => s.Settings).Returns(new AppSettings());

        Mock<IFileImportService> fileImportService = new Mock<IFileImportService>();
        Mock<IPlaylistManagerService> playlistManagerService = new Mock<IPlaylistManagerService>();
        playlistManagerService.Setup(p => p.LoadPlaylistTracks(It.IsAny<string>())).Returns(new List<MediaFile>());

        Mock<ITrackNavigationService> trackNavigationService = new Mock<ITrackNavigationService>();
        Mock<IUiDispatcher> uiDispatcher = new Mock<IUiDispatcher>();
        Mock<IDatabaseSaveService> databaseSaveService = new Mock<IDatabaseSaveService>();
        Mock<ISelectionService> selectionService = new Mock<ISelectionService>();
        selectionService.SetupGet(s => s.CurrentTrack).Returns((MediaFile?)null);

        Mock<IPlaybackCoordinator> playbackCoordinator = new Mock<IPlaybackCoordinator>();
        ILogger<PlaylistTabsViewModel> logger = Mock.Of<ILogger<PlaylistTabsViewModel>>();

        return new PlaylistTabsViewModel(
            musicLibrary.Object,
            shared.Object,
            settingsManager.Object,
            fileImportService.Object,
            playlistManagerService.Object,
            trackNavigationService.Object,
            uiDispatcher.Object,
            databaseSaveService.Object,
            selectionService.Object,
            playbackCoordinator.Object,
            logger);
    }
}
