using System.Reflection;
using FluentAssertions;
using Moq;
using LinkerPlayer.ViewModels;
using LinkerPlayer.Services;
using LinkerPlayer.Models;
using LinkerPlayer.Core;
using Microsoft.Extensions.Logging;
using System.Windows.Input;
using LinkerPlayer.Tests.Mocks;

namespace LinkerPlayer.Tests.ViewModels;

public class PlaylistTabsViewModel_DragDropTests
{
    private static PlaylistTabsViewModel CreateViewModel()
    {
        Mock<IMusicLibrary> musicLibrary = new Mock<IMusicLibrary>(MockBehavior.Strict);
        musicLibrary.SetupGet(m => m.Playlists).Returns(new System.Collections.ObjectModel.ObservableCollection<Playlist>());
        musicLibrary.SetupGet(m => m.MainLibrary).Returns(new System.Collections.ObjectModel.ObservableCollection<MediaFile>());

        Mock<ISettingsManager> settings = new Mock<ISettingsManager>();
        settings.SetupGet(s => s.Settings).Returns(new AppSettings());
        settings.Setup(s => s.SaveSettings(It.IsAny<string>()));

        Mock<IFileImportService> fileImport = new Mock<IFileImportService>();
        Mock<IPlaylistManagerService> playlistManager = new Mock<IPlaylistManagerService>();
        Mock<ITrackNavigationService> trackNav = new Mock<ITrackNavigationService>();

        Mock<IUiDispatcher> uiDispatcher = new Mock<IUiDispatcher>();
        uiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Action>())).Returns<Action>(a => { a(); return Task.CompletedTask; });
        uiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Func<Task>>())).Returns<Func<Task>>(async f => await f());
        uiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Func<object>>())).Returns<Func<object>>(f => Task.FromResult(f()));
        uiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Func<Task<object>>>())).Returns<Func<Task<object>>>(async f => await f());
        uiDispatcher.Setup(d => d.CheckAccess()).Returns(true);

        Mock<IDatabaseSaveService> dbSave = new Mock<IDatabaseSaveService>();
        ILogger<PlaylistTabsViewModel> logger = Mock.Of<ILogger<PlaylistTabsViewModel>>();

        SharedDataModel shared = new SharedDataModel();
        ISelectionService selection = new TestSelectionService();

        return new PlaylistTabsViewModel(
            musicLibrary.Object,
            shared,
            settings.Object,
            fileImport.Object,
            playlistManager.Object,
            trackNav.Object,
            uiDispatcher.Object,
            dbSave.Object,
            selection,
            logger);
    }

    [Fact]
    public void DragDrop_Commands_AreGenerated()
    {
        PlaylistTabsViewModel vm = CreateViewModel();
        Type type = vm.GetType();

        PropertyInfo? dragOverProp = type.GetProperty("DragOverCommand", BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo? dropProp = type.GetProperty("DropCommand", BindingFlags.Public | BindingFlags.Instance);

        dragOverProp.Should().NotBeNull();
        dropProp.Should().NotBeNull();

        object? dragOverCmdObj = dragOverProp!.GetValue(vm);
        object? dropCmdObj = dropProp!.GetValue(vm);

        dragOverCmdObj.Should().NotBeNull();
        dropCmdObj.Should().NotBeNull();

        // Verify ICommand is implemented and CanExecute returns true
        ICommand? dragOverCmd = dragOverCmdObj as ICommand;
        ICommand? dropCmd = dropCmdObj as ICommand;

        dragOverCmd.Should().NotBeNull();
        dropCmd.Should().NotBeNull();

        dragOverCmd!.CanExecute(null).Should().BeTrue();
        dropCmd!.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ExtractPathsFromM3u_ParsesBasicEntries()
    {
        // Arrange
        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(tmp, new[]
            { "#EXTM3U", "C:/Music/Song1.mp3","","#EXTINF:123, Some Artist - Some Title","C:/Music/Album/Song2.flac"});

            // Act
            MethodInfo? method = typeof(PlaylistTabsViewModel).GetMethod("ExtractPathsFromM3u", BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();
            List<string> result = (List<string>)method!.Invoke(null, new object[] { tmp })!;

            // Assert
            result.Should().Contain(new[] { "C:/Music/Song1.mp3", "C:/Music/Album/Song2.flac" });
        }
        finally
        {
            try
            {
                File.Delete(tmp);
            }
            catch { }
        }
    }
}
