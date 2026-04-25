using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.Tests.Fakes;
using LinkerPlayer.UserControls;
using LinkerPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using System.Reflection;
using System.Windows.Controls;

namespace LinkerPlayer.Tests.ViewModels;

public class MusicLibraryColumnPersistenceTests
{
    [StaFact]
    public void OnUpdateColumns_PersistsLibraryVisibleColumns()
    {
        // Arrange
        FakeSettingsManager settings = new FakeSettingsManager();

        // Create a minimal PlaylistTabsViewModel with default dependencies where possible
        // We'll only need the Settings via App.AppHost in PlaylistTabs code; instead we can
        // directly create PlaylistTabs and call private methods via reflection.

        PlaylistTabs playlistTabs = new PlaylistTabs();

        // Create a MusicLibraryTab and attach it to a DataGrid
        MusicLibraryTab libTab = new MusicLibraryTab(new System.Collections.ObjectModel.ObservableCollection<MediaFile>());
        DataGrid dg = new DataGrid { DataContext = libTab };

        // Inject a TabControl containing our DataGrid into the PlaylistTabs private field Tabs123
        TabControl tabControl = new TabControl();
        TabItem tabItem = new TabItem { Content = dg };
        tabControl.Items.Add(tabItem);

        // Set the private Tabs123 field
        FieldInfo? tabsField = typeof(PlaylistTabs).GetField("Tabs123", BindingFlags.NonPublic | BindingFlags.Instance);
        tabsField!.SetValue(playlistTabs, tabControl);

        // Ensure settings manager is reachable via App.AppHost.Services if used; use reflection to set App.AppHost.Services if necessary
        // Simpler approach: call OnUpdateColumns via reflection which uses GetActiveDataGrid and saves via App.AppHost?.Services
        // To make settings discoverable, set App.AppHost to a minimal host containing ISettingsManager service.

        // Create a Host with ISettingsManager registered so PlaylistTabs code can locate it via App.AppHost
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(sc => sc.AddSingleton<Core.ISettingsManager>(settings))
            .Build();
        typeof(App).GetProperty("AppHost")!.SetValue(null, host);

        // Act - call private OnUpdateColumns(UpdateColumnsMessage)
        UpdateColumnsMessage msg = new LinkerPlayer.Messages.UpdateColumnsMessage(new List<string> { "Title", "Artist" });
        MethodInfo? onUpdate = typeof(PlaylistTabs).GetMethod("OnUpdateColumns", BindingFlags.NonPublic | BindingFlags.Instance);
        onUpdate!.Invoke(playlistTabs, new object[] { msg });

        // Assert
        settings.Settings.LibraryVisibleColumns.Count.ShouldBe(2);
        settings.Settings.LibraryVisibleColumns.ShouldBe(new List<string> { "Title", "Artist" });
        libTab.VisibleColumns.ShouldBe(new List<string> { "Title", "Artist" });
    }

    [StaFact]
    public void ColumnLayoutSaveTimer_PersistsLibraryColumnSettings()
    {
        // Arrange
        FakeSettingsManager settings = new FakeSettingsManager();
        IHost host2 = Host.CreateDefaultBuilder()
            .ConfigureServices(sc => sc.AddSingleton<Core.ISettingsManager>(settings))
            .Build();
        typeof(App).GetProperty("AppHost")!.SetValue(null, host2);

        PlaylistTabs playlistTabs = new PlaylistTabs();

        MusicLibraryTab libTab = new MusicLibraryTab(new System.Collections.ObjectModel.ObservableCollection<MediaFile>());
        DataGrid dg = new DataGrid { DataContext = libTab };

        // Create columns: first a template column (icon) then two text columns with bindings
        DataGridTemplateColumn icon = new DataGridTemplateColumn();
        dg.Columns.Add(icon);

        DataGridTextColumn titleCol = new DataGridTextColumn();
        titleCol.Binding = new System.Windows.Data.Binding("Title");
        titleCol.DisplayIndex = 1;
        dg.Columns.Add(titleCol);

        DataGridTextColumn artistCol = new DataGridTextColumn();
        artistCol.Binding = new System.Windows.Data.Binding("Artist");
        artistCol.DisplayIndex = 2;
        dg.Columns.Add(artistCol);

        // Inject tab control
        TabControl tabControl = new TabControl();
        TabItem tabItem = new TabItem { Content = dg };
        tabControl.Items.Add(tabItem);
        FieldInfo? tabsField = typeof(PlaylistTabs).GetField("Tabs123", BindingFlags.NonPublic | BindingFlags.Instance);
        tabsField!.SetValue(playlistTabs, tabControl);

        // Also set DataContext to PlaylistTabsViewModel with SelectedTab being libTab so save logic recognizes it
        // Create a minimal PlaylistTabsViewModel with mocks similar to other tests
        Mock<IMusicLibrary> mockLibrary = new Mock<LinkerPlayer.Core.IMusicLibrary>();
        mockLibrary.SetupGet(m => m.MainLibrary).Returns(new RangeObservableCollection<MediaFile>());
        Mock<ISharedDataModel> mockShared = new Mock<LinkerPlayer.ViewModels.ISharedDataModel>();
        Mock<IFileImportService> mockFileImport = new Mock<LinkerPlayer.Services.IFileImportService>();
        Mock<IPlaylistManagerService> mockPlaylist = new Mock<LinkerPlayer.Services.IPlaylistManagerService>();
        Mock<ITrackNavigationService> mockNav = new Mock<LinkerPlayer.Services.ITrackNavigationService>();
        Mock<IUiDispatcher> mockUi = new Mock<LinkerPlayer.Services.IUiDispatcher>();
        Mock<IDatabaseSaveService> mockSave = new Mock<LinkerPlayer.Services.IDatabaseSaveService>();
        Mock<ISelectionService> mockSelection = new Mock<LinkerPlayer.Services.ISelectionService>();
        Mock<ILogger<PlaylistTabsViewModel>> mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<PlaylistTabsViewModel>>();

        PlaylistTabsViewModel vm = new PlaylistTabsViewModel(
            mockLibrary.Object,
            mockShared.Object,
            settings,
            mockFileImport.Object,
            mockPlaylist.Object,
            mockNav.Object,
            mockUi.Object,
            mockSave.Object,
            mockSelection.Object,
            new LinkerPlayer.Tests.Mocks.TestPlaybackCoordinator(),
            Mock.Of<IImportCancellationService>(),
            mockLogger.Object);

        // use reflection to set DataContext
        PropertyInfo? dcProp = typeof(PlaylistTabs).GetProperty("DataContext");
        dcProp!.SetValue(playlistTabs, vm);

        // Ensure vm.TabList contains libTab and vm.SelectedTab references it
        vm.TabList.Clear();
        vm.TabList.Add(libTab);
        vm.SelectedTabIndex = 0;
        vm.SelectedTab = libTab;

        // Act - call private ColumnLayoutSaveTimer_Tick
        MethodInfo? tick = typeof(PlaylistTabs).GetMethod("ColumnLayoutSaveTimer_Tick", BindingFlags.NonPublic | BindingFlags.Instance);
        tick!.Invoke(playlistTabs, [null, System.EventArgs.Empty]);

        // Assert - settings should have library column info
        settings.Settings.LibraryColumnSettings.ShouldContainKey("Title");
        settings.Settings.LibraryColumnSettings.ShouldContainKey("Artist");
        settings.Settings.LibraryVisibleColumns.SequenceEqual(new[] { "Title", "Artist" }).ShouldBeTrue();
    }
}
