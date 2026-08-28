using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace LinkerPlayer.Services;

public class RescanLogger : IRescanLogger
{
    private readonly ISettingsManager _settingsManager;

    public ObservableCollection<RescanLogEntry> Entries { get; } = new();

    public bool IsScanning { get; private set; }

    public DateTime? LastScanCompletedUtc { get; private set; }

    public string ScanStatusText { get; private set; } = string.Empty;

    public event EventHandler? ScanStatusChanged;

    public RescanLogger(ISettingsManager settingsManager)
    {
        _settingsManager = settingsManager;

        // Restore last-scan timestamp from persisted settings
        if (_settingsManager.Settings.LastScanCompletedUtc.HasValue)
        {
            LastScanCompletedUtc = _settingsManager.Settings.LastScanCompletedUtc;
            ScanStatusText = FormatLastScanText(LastScanCompletedUtc.Value);
        }
    }

    public void BeginSession()
    {
        // Set state synchronously so RefreshScanStatus() reads correct values
        // whenever the Settings window is opened — even if opened after the scan started.
        IsScanning = true;
        ScanStatusText = "Scanning watch folders\u2026";

        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            Entries.Clear();
            ScanStatusChanged?.Invoke(this, EventArgs.Empty);
            TryShowWindow();
        }, DispatcherPriority.Background);
    }

    public void EndSession()
    {
        IsScanning = false;
        LastScanCompletedUtc = DateTime.UtcNow;

        // Persist to settings
        _settingsManager.Settings.LastScanCompletedUtc = LastScanCompletedUtc;
        _settingsManager.SaveSettings(nameof(AppSettings.LastScanCompletedUtc));

        // Update text synchronously so any poll/refresh sees the right value immediately.
        ScanStatusText = FormatLastScanText(LastScanCompletedUtc.Value);

        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            ScanStatusChanged?.Invoke(this, EventArgs.Empty);
            RescanLogWindow? wnd = App.AppHost?.Services?.GetService<RescanLogWindow>();
            wnd?.StopSpinner();
        }, DispatcherPriority.Background);
    }

    public void LogInfo(string detail)    => Append(RescanAction.Info,    detail);
    public void LogAdded(string detail)   => Append(RescanAction.Added,   detail);
    public void LogRemoved(string detail) => Append(RescanAction.Removed, detail);
    public void LogUpdated(string detail) => Append(RescanAction.Updated, detail);
    public void LogWarning(string detail) => Append(RescanAction.Warning, detail);

    private void Append(RescanAction action, string detail)
    {
        RescanLogEntry entry = new RescanLogEntry { Action = action, Detail = detail, Time = DateTime.Now };
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            Entries.Add(entry);
        }, DispatcherPriority.Background);
    }

    private static string FormatLastScanText(DateTime utc)
    {
        DateTime local = utc.ToLocalTime();
        // "Last scan: today at 2:34 PM" or "Last scan: Mon Jan 6 at 2:34 PM"
        string datePart = local.Date == DateTime.Today
            ? "today"
            : local.ToString("ddd MMM d");
        return $"Last scan: {datePart} at {local:h:mm tt}";
    }

    private void TryShowWindow()
    {
        try
        {
            RescanLogWindow? wnd = App.AppHost?.Services?.GetService<RescanLogWindow>();
            if (wnd == null) return;

            // Re-evaluate owner every session: prefer SettingsWindow when visible
            SettingsWindow? settingsWindow = App.AppHost?.Services?.GetService<SettingsWindow>();
            Window? preferredOwner = (settingsWindow?.IsVisible == true ? settingsWindow : null)
                ?? Application.Current?.MainWindow;

            if (wnd.Owner != preferredOwner)
                wnd.Owner = preferredOwner;

            if (!wnd.IsVisible)
            {
                // Center over the owner manually (WindowStartupLocation=Manual)
                if (preferredOwner != null)
                {
                    wnd.Left = preferredOwner.Left + (preferredOwner.ActualWidth  - wnd.Width)  / 2;
                    wnd.Top  = preferredOwner.Top  + (preferredOwner.ActualHeight - wnd.Height) / 2;
                }
                wnd.Show();
            }
            else
            {
                wnd.Activate();
            }

            wnd.StartSpinner();
        }
        catch { }
    }
}
