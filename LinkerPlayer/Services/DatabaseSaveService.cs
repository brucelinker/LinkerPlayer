using LinkerPlayer.Core;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace LinkerPlayer.Services;

/// <summary>
/// Debounced database save service that batches save operations to prevent
/// blocking the UI thread on every user action.
/// </summary>
public class DatabaseSaveService : IDatabaseSaveService
{
    private readonly IMusicLibrary _musicLibrary;
    private readonly ILogger<DatabaseSaveService> _logger;
    private readonly Timer _debounceTimer;
    private readonly object _lock = new object();
    private bool _saveRequested;
    private bool _disposed;

    // Debounce delay: Wait 2 seconds after last change before saving
    private const int DebounceDelayMs = 2000;

    public DatabaseSaveService(
        IMusicLibrary musicLibrary,
        ILogger<DatabaseSaveService> logger)
    {
        _musicLibrary = musicLibrary ?? throw new ArgumentNullException(nameof(musicLibrary));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create timer but don't start it yet
        _debounceTimer = new Timer(OnTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Requests a deferred database save. The save will be executed after a short delay
    /// to batch multiple changes together.
    /// </summary>
    public void RequestSave()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            _saveRequested = true;

            // Reset the timer - this delays the save by another 2 seconds
            _debounceTimer.Change(DebounceDelayMs, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Forces an immediate save to the database, bypassing the debounce delay.
    /// </summary>
    public void SaveImmediately()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            // Cancel any pending debounced save
            _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);

            if (_saveRequested)
            {
                ExecuteSave();
                _saveRequested = false;
            }
            else
            {
                _logger.LogDebug("SaveImmediately called but no changes pending");
            }
        }
    }

    private void OnTimerElapsed(object? state)
    {
        lock (_lock)
        {
            if (_saveRequested && !_disposed)
            {
                ExecuteSave();
                _saveRequested = false;
            }
        }
    }

    private void ExecuteSave()
    {
        try
        {
            Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _musicLibrary.SaveToDatabase();
            stopwatch.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save database");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            _disposed = true;

            // Save any pending changes before disposing
            if (_saveRequested)
            {
                _logger.LogInformation("Disposing DatabaseSaveService - saving pending changes");
                ExecuteSave();
            }

            _debounceTimer?.Dispose();
            _logger.LogInformation("DatabaseSaveService disposed");
        }
    }
}
