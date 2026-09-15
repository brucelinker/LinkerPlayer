using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.Tests.Fakes;
using LinkerPlayer.Tests.Mocks;
using LinkerPlayer.UserControls;
using LinkerPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Data;

namespace LinkerPlayer.Tests.ViewModels;

public sealed class MusicLibraryColumnPersistenceTests : IDisposable
{
    private readonly IHost? _originalAppHost = App.AppHost;

    public void Dispose()
    {
        App.AppHost = _originalAppHost!;
    }

    [StaFact]
    public void OnUpdateColumns_PersistsLibraryVisibleColumns()
    {
        FakeSettingsManager settings = new();
        App.AppHost = null!;

        PlaylistTabs control = new PlaylistTabs();
        App.AppHost = CreateHost(settings);
        MediaTabViewModel vm = CreateViewModel(settings, out _);
        control.DataContext = vm;

        PlaylistTab playlistTab = new() { Name = "P1" };
        DataGrid dataGrid = new() { DataContext = playlistTab };
        SetActiveTab(control, dataGrid);

        InvokePrivate(control, "OnUpdateColumns", new UpdateColumnsMessage(new List<string> { "Title", "Artist" }));

        settings.Settings.VisibleColumns.ShouldBe(new List<string> { "Title", "Artist" });
        dataGrid.Columns.Count.ShouldBeGreaterThanOrEqualTo(3);
        dataGrid.Columns[1].Header.ShouldBe("Title");
        dataGrid.Columns[2].Header.ShouldBe("Artist");
    }

    [StaFact]
    public void ColumnLayoutSaveTimer_PersistsLibraryColumnSettings()
    {
        FakeSettingsManager settings = new();
        App.AppHost = null!;

        PlaylistTabs control = new();
        App.AppHost = CreateHost(settings);
        MediaTabViewModel vm = CreateViewModel(settings, out _);
        control.DataContext = vm;

        LibraryTab libraryTab = new(new ObservableCollection<MediaFile>());
        vm.SelectedTab = libraryTab;

        DataGrid dataGrid = new() { DataContext = libraryTab };
        dataGrid.Columns.Add(new DataGridTemplateColumn());
        dataGrid.Columns.Add(new DataGridTextColumn { Binding = new Binding("Title") });
        dataGrid.Columns.Add(new DataGridTextColumn { Binding = new Binding("Artist") });
        SetActiveTab(control, dataGrid);

        InvokePrivate(control, "ColumnLayoutSaveTimer_Tick", null, EventArgs.Empty);

        settings.Settings.LibraryVisibleColumns.ShouldBe(new List<string> { "Title", "Artist" });
        settings.Settings.LibraryColumnSettings.ShouldContainKey("Title");
        settings.Settings.LibraryColumnSettings.ShouldContainKey("Artist");
        libraryTab.VisibleColumns.ShouldBe(new List<string> { "Title", "Artist" });
    }

    private static MediaTabViewModel CreateViewModel(FakeSettingsManager settings, out Mock<IPlaylistManagerService> playlistManager)
    {
        FakeMusicLibrary musicLibrary = new();
        SharedDataModel sharedDataModel = new();
        SelectionService selectionService = new(sharedDataModel, Mock.Of<ILogger<SelectionService>>());
        TestPlaybackCoordinator playbackCoordinator = new();

        Mock<IUiDispatcher> uiDispatcher = new();
        uiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Action>()))
            .Returns<Action>(action => { action(); return Task.CompletedTask; });
        uiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Func<Task>>()))
            .Returns<Func<Task>>(async asyncAction => await asyncAction());
        uiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Func<object>>()))
            .Returns<Func<object>>(func => Task.FromResult(func()));
        uiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Func<Task<object>>>()))
            .Returns<Func<Task<object>>>(async asyncFunc => await asyncFunc());
        uiDispatcher.Setup(d => d.CheckAccess()).Returns(true);

        playlistManager = new Mock<IPlaylistManagerService>();
        LibraryTabViewModel libraryTabViewModel = new(
            musicLibrary,
            settings,
            selectionService,
            playbackCoordinator,
            Mock.Of<ILogger<LibraryTabViewModel>>());

        MediaTabViewModel viewModel = new(
            musicLibrary,
            sharedDataModel,
            settings,
            Mock.Of<IFileImportService>(),
            playlistManager.Object,
            Mock.Of<ITrackNavigationService>(),
            uiDispatcher.Object,
            Mock.Of<IDatabaseSaveService>(),
            selectionService,
            playbackCoordinator,
            Mock.Of<IPlaylistFileService>(),
            Mock.Of<IImportCancellationService>(),
            Mock.Of<IMusicBrainzRatingService>(),
            libraryTabViewModel,
            Mock.Of<ILogger<MediaTabViewModel>>());

        return viewModel;
    }

    private static IHost CreateHost(ISettingsManager settings)
    {
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton(settings)
            .BuildServiceProvider();

        Mock<IHost> host = new();
        host.SetupGet(h => h.Services).Returns(provider);
        return host.Object;
    }

    private static void SetActiveTab(PlaylistTabs control, DataGrid dataGrid)
    {
        CachingTabControl tabs = new();
        TabItem item = new() { Content = dataGrid };
        tabs.Items.Add(item);
        tabs.SelectedItem = item;

        SetPrivateField(control, "Tabs123", tabs);
    }

    private static void InvokePrivate(object target, string methodName, params object?[] args)
    {
        MethodInfo? method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        method.ShouldNotBeNull(methodName);
        method!.Invoke(target, args);
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        FieldInfo? field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        field.ShouldNotBeNull(fieldName);
        field!.SetValue(target, value);
    }
}
