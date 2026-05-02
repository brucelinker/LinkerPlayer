using System.Collections.ObjectModel;
using LinkerPlayer.Models;

namespace LinkerPlayer.Services;

public interface IRescanLogger
{
    ObservableCollection<RescanLogEntry> Entries { get; }

    void LogInfo(string detail);
    void LogAdded(string detail);
    void LogRemoved(string detail);
    void LogUpdated(string detail);
    void LogWarning(string detail);

    /// <summary>Called at the start of a rescan to optionally clear old entries and show the window.</summary>
    void BeginSession();

    /// <summary>Called when the rescan is complete. Stops the activity spinner.</summary>
    void EndSession();

    /// <summary>True while a rescan is in progress.</summary>
    bool IsScanning { get; }

    /// <summary>UTC timestamp of the last completed scan. Null if never scanned this session.</summary>
    DateTime? LastScanCompletedUtc { get; }

    /// <summary>Human-readable status for display in the Settings Library page.</summary>
    string ScanStatusText { get; }

    /// <summary>Raised on the UI thread when ScanStatusText changes.</summary>
    event EventHandler? ScanStatusChanged;
}
