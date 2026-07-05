//using System.Reflection;
//using Shouldly;
//using Moq;
//using LinkerPlayer.ViewModels;
//using LinkerPlayer.Services;
//using LinkerPlayer.Models;
//using LinkerPlayer.Core;
//using LinkerPlayer.Services.Playback;
//using Microsoft.Extensions.Logging;
//using System.Windows.Input;
//using LinkerPlayer.Tests.Mocks;

//namespace LinkerPlayer.Tests.ViewModels;

//public class PlaylistTabsViewModel_DragDropTests
//{
//    private static PlaylistTabsViewModel CreateViewModel()
//    {
//        Mock<IMusicLibrary> _mockMusicLibrary = new Mock<IMusicLibrary>(MockBehavior.Strict);
//        _mockMusicLibrary.SetupGet(m => m.Playlists).Returns(new System.Collections.ObjectModel.ObservableCollection<Playlist>());
//        _mockMusicLibrary.SetupGet(m => m.MainLibrary).Returns(new RangeObservableCollection<MediaFile>());

//        Mock<ISettingsManager> _mockSettingsManager = new Mock<ISettingsManager>();
//        _mockSettingsManager.SetupGet(s => s.Settings).Returns(new AppSettings());
//        _mockSettingsManager.Setup(s => s.SaveSettings(It.IsAny<string>()));

//        Mock<IFileImportService> _mockFileImportService = new Mock<IFileImportService>();
//        Mock<IPlaylistManagerService> _mockPlaylistManagerService = new Mock<IPlaylistManagerService>();
//        Mock<ITrackNavigationService> _mockTrackNavigationService = new Mock<ITrackNavigationService>();

//        Mock<IUiDispatcher> _mockUiDispatcher = new Mock<IUiDispatcher>();
//        _mockUiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Action>())).Returns<Action>(a => { a(); return Task.CompletedTask; });
//        _mockUiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Func<Task>>())).Returns<Func<Task>>(async f => await f());
//        _mockUiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Func<object>>())).Returns<Func<object>>(f => Task.FromResult(f()));
//        _mockUiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Func<Task<object>>>())).Returns<Func<Task<object>>>(async f => await f());
//        _mockUiDispatcher.Setup(d => d.CheckAccess()).Returns(true);

//        Mock<IDatabaseSaveService> _mockDbSave = new Mock<IDatabaseSaveService>();
//        Mock<IMusicBrainzRatingService> _mockMbService = new Mock<IMusicBrainzRatingService>();
//        Mock<ILogger<PlaylistTabsViewModel>> _mockLogger = new Mock<ILogger<PlaylistTabsViewModel>>();

//        SharedDataModel shared = new SharedDataModel();
//        ISelectionService selection = new TestSelectionService();
//        IPlaybackCoordinator playbackCoordinator = new TestPlaybackCoordinator();

//        return new PlaylistTabsViewModel(
//            _mockMusicLibrary.Object,
//            shared,
//            _mockSettingsManager.Object,
//            _mockFileImportService.Object,
//            _mockPlaylistManagerService.Object,
//            _mockTrackNavigationService.Object,
//            _mockUiDispatcher.Object,
//            _mockDbSave.Object,
//            selection,
//            playbackCoordinator,
//            Mock.Of<IImportCancellationService>(),
//            _mockMbService.Object,
//            _mockLogger.Object);
//    }

//    [StaFact]
//    public void DragDrop_Commands_AreGenerated()
//    {
//        PlaylistTabsViewModel vm = CreateViewModel();
//        Type type = vm.GetType();

//        PropertyInfo? dragOverProp = type.GetProperty("DragOverCommand", BindingFlags.Public | BindingFlags.Instance);
//        PropertyInfo? dropProp = type.GetProperty("DropCommand", BindingFlags.Public | BindingFlags.Instance);

//        dragOverProp.ShouldNotBeNull();
//        dropProp.ShouldNotBeNull();

//        object? dragOverCmdObj = dragOverProp!.GetValue(vm);
//        object? dropCmdObj = dropProp!.GetValue(vm);

//        dragOverCmdObj.ShouldNotBeNull();
//        dropCmdObj.ShouldNotBeNull();

//        // Verify ICommand is implemented and CanExecute returns true
//        ICommand? dragOverCmd = dragOverCmdObj as ICommand;
//        ICommand? dropCmd = dropCmdObj as ICommand;

//        dragOverCmd.ShouldNotBeNull();
//        dropCmd.ShouldNotBeNull();

//        dragOverCmd!.CanExecute(null).ShouldBeTrue();
//        dropCmd!.CanExecute(null).ShouldBeTrue();
//    }

//    [StaFact]
//    public void ExtractPathsFromM3u_ParsesBasicEntries()
//    {
//        // Arrange
//        string tmp = Path.GetTempFileName();
//        try
//        {
//            File.WriteAllLines(tmp, new[]
//            { "#EXTM3U", "C:/Music/Song1.mp3","","#EXTINF:123, Some Artist - Some Title","C:/Music/Album/Song2.flac"});

//            // Act
//            MethodInfo? method = typeof(PlaylistTabsViewModel).GetMethod("ExtractPathsFromM3u", BindingFlags.NonPublic | BindingFlags.Static);
//            method.ShouldNotBeNull();
//            List<string> result = (List<string>)method!.Invoke(null, new object[] { tmp })!;

//            // Assert
//            result.ShouldContain("C:/Music/Song1.mp3");
//            result.ShouldContain("C:/Music/Album/Song2.flac");
//        }
//        finally
//        {
//            try
//            {
//                File.Delete(tmp);
//            }
//            catch { }
//        }
//    }
//}
