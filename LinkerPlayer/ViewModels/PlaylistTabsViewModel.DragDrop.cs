using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Windows;

namespace LinkerPlayer.ViewModels;

public partial class PlaylistTabsViewModel
{
 [RelayCommand]
 private void DragOver(DragEventArgs args)
 {
 if (args.Data.GetDataPresent(DataFormats.FileDrop))
 {
 args.Effects = DragDropEffects.Copy;
 args.Handled = true;
 }
 else
 {
 args.Effects = DragDropEffects.None;
 args.Handled = true;
 }
 }

 [RelayCommand]
 private async Task Drop(DragEventArgs args)
 {
 if (!args.Data.GetDataPresent(DataFormats.FileDrop))
 {
 args.Handled = true;
 _logger.LogWarning("Drop event triggered without FileDrop data");
 return;
 }

 string[] droppedItems = (string[])args.Data.GetData(DataFormats.FileDrop)!;
 bool isControlPressed = (args.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;

 Progress<ProgressData> progress = new Progress<ProgressData>(data =>
 {
 WeakReferenceMessenger.Default.Send(new ProgressValueMessage(data));
 });

 await HandleDropAsync(droppedItems, isControlPressed, progress);
 args.Handled = true;
 }

 private async Task HandleDropAsync(string[] droppedItems, bool createNewPlaylist, IProgress<ProgressData> progress)
 {
 try
 {
 foreach (string item in droppedItems)
 {
 if (File.Exists(item) && _fileImportService.IsAudioFile(item))
 {
 await HandleSingleFileDropAsync(item);
 }
 else if (Directory.Exists(item))
 {
 await HandleFolderDropAsync(item, createNewPlaylist, progress);
 }
 else
 {
 _logger.LogWarning("Invalid drop item: {Item}", item);
 }
 }
 }
 catch (Exception ex)
 {
 _logger.LogError(ex, "Failed to process dropped items");
 await _uiDispatcher.InvokeAsync(() =>
 {
 progress.Report(new ProgressData
 {
 IsProcessing = false,
 Status = "Error processing dropped items"
 });
 });
 }
 }

 private async Task HandleSingleFileDropAsync(string filePath)
 {
 try
 {
 MediaFile? importedFile = await _fileImportService.ImportFileAsync(filePath);
 if (importedFile != null)
 {
 await EnsureSelectedTabExistsAsync();

 if (SelectedTabIndex >=0 && SelectedTabIndex < TabList.Count)
 {
 var tab = TabList[SelectedTabIndex];
 await _playlistManagerService.AddTracksToPlaylistAsync(tab.Name, new[] { importedFile });

 await _uiDispatcher.InvokeAsync(() =>
 {
 tab.Tracks.Add(importedFile); // ObservableCollection auto-notifies!
 });
 }
 }
 }
 catch (Exception ex)
 {
 _logger.LogError(ex, "Failed to process dropped file: {FilePath}", filePath);
 }
 }

 private async Task HandleFolderDropAsync(string folderPath, bool createNewPlaylist, IProgress<ProgressData> progress)
 {
 if (createNewPlaylist)
 {
 await CreatePlaylistFromFolderAsync(folderPath, progress);
 }
 else
 {
 await AddFolderToCurrentPlaylistAsync(folderPath, progress);
 }
 }

 private async Task AddFolderToCurrentPlaylistAsync(string folderPath, IProgress<ProgressData>? progress = null)
 {
 if (_dataGrid == null)
 {
 _logger.LogError("DataGrid is null, cannot add folder to current playlist");
 return;
 }

 try
 {
 await EnsureSelectedTabExistsAsync();

 List<MediaFile> importedTracks = await _fileImportService.ImportFolderAsync(folderPath, progress);

 if (!importedTracks.Any())
 {
 _logger.LogWarning("No tracks imported from folder: {FolderPath}", folderPath);
 return;
 }

 if (SelectedTabIndex >=0 && SelectedTabIndex < TabList.Count)
 {
 var tab = TabList[SelectedTabIndex];
 bool success = await _playlistManagerService.AddTracksToPlaylistAsync(tab.Name, importedTracks);

 if (success)
 {
 await _uiDispatcher.InvokeAsync(() =>
 {
 foreach (MediaFile track in importedTracks)
 {
 if (tab.Tracks.All(t => t.Id != track.Id))
 {
 tab.Tracks.Add(track); // ObservableCollection auto-notifies!
 }
 }

 if (_dataGrid.SelectedItem == null && tab.Tracks.Any())
 {
 _dataGrid.SelectedIndex =0;
 _dataGrid.ScrollIntoView(_dataGrid.SelectedItem!);
 }
 });
 }
 else
 {
 _logger.LogError("Failed to add tracks to playlist");
 await _uiDispatcher.InvokeAsync(() =>
 {
 progress?.Report(new ProgressData
 {
 IsProcessing = false,
 Status = "Error adding tracks to playlist"
 });
 });
 }
 }
 }
 catch (Exception ex)
 {
 _logger.LogError(ex, "Failed to process folder: {FolderPath}", folderPath);
 await _uiDispatcher.InvokeAsync(() =>
 {
 progress?.Report(new ProgressData
 {
 IsProcessing = false,
 Status = "Error processing folder"
 });
 });
 }
 }
}
