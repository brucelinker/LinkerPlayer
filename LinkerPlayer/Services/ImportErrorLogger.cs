using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using LinkerPlayer.Interop;
using LinkerPlayer.Models;
using LinkerPlayer.Windows;

namespace LinkerPlayer.Services;

public class ImportErrorLogger : IImportErrorLogger
{
    private readonly ILogger<ImportErrorLogger> _logger;
    public ObservableCollection<ImportErrorEntry> Errors { get; } = new ObservableCollection<ImportErrorEntry>();

    public ImportErrorLogger(ILogger<ImportErrorLogger> logger)
    {
        _logger = logger;
    }

    public void Log(string path, string message)
    {
        ImportErrorEntry entry = new ImportErrorEntry { Path = path, Message = message, Time = DateTime.UtcNow };
        _logger.LogWarning("Import error: {Path} - {Message}", path, message);
        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            Errors.Add(entry);
            TryShowWindow();
        }), DispatcherPriority.Background);
    }

    public void Log(string path, Exception ex)
    {
        ImportErrorEntry entry = new ImportErrorEntry { Path = path, Message = ex.Message, Time = DateTime.UtcNow };
        _logger.LogWarning(ex, "Import exception for {Path}: {Message}", path, ex.Message);
        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            Errors.Add(entry);
            TryShowWindow();
        }), DispatcherPriority.Background);
    }

    private void TryShowWindow()
    {
        try
        {
            // Resolve window from DI if available
            ImportErrorsWindow? wnd = App.AppHost?.Services?.GetService<ImportErrorsWindow>();
            if (wnd == null)
            {
                // Fallback: no dedicated import errors window registered - show a simple message box
                try
                {
                    ImportErrorEntry? latest = Errors.LastOrDefault();
                    if (latest != null)
                    {
                        MessageBox.Show($"Import error:\n{latest.Path}\n{latest.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch { }
                return;
            }

            if (wnd.Owner == null)
            {
                wnd.Owner = Application.Current?.MainWindow;
            }

            OwnedWindowHelper.Show(wnd, Application.Current?.MainWindow);
        }
        catch
        {
            // Swallow any UI exceptions to avoid interfering with import flow
        }
    }
}
