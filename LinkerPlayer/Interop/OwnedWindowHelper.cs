using System;
using System.Windows;
using LinkerPlayer.Core;
using LinkerPlayer.Models;

namespace LinkerPlayer.Interop;

public static class OwnedWindowHelper
{
    public static void RestoreBounds(Window window, ISettingsManager settingsManager, string settingsKey, Window? owner)
    {
        if (settingsManager.Settings.WindowBounds.TryGetValue(settingsKey, out AppSettings.WindowBoundsSettings? bounds)
            && bounds.Width > 0
            && bounds.Height > 0)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Width = bounds.Width;
            window.Height = bounds.Height;
            window.Left = bounds.Left;
            window.Top = bounds.Top;
            return;
        }

        if (owner != null)
        {
            CenterOnOwner(window, owner);
        }
    }

    public static void SaveBounds(Window window, ISettingsManager settingsManager, string settingsKey)
    {
        Rect bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
            : window.RestoreBounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        settingsManager.Settings.WindowBounds[settingsKey] = new AppSettings.WindowBoundsSettings
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height
        };

        settingsManager.SaveSettings(nameof(AppSettings.WindowBounds));
    }

    public static void RegisterPlacement(Window window, string settingsKey, Action? onFailure = null)
    {
        try
        {
            ((App)Application.Current).WindowPlace.Register(window, settingsKey);
        }
        catch
        {
            onFailure?.Invoke();
        }
    }

    public static void Show(Window window, Window? owner)
    {
        if (owner != null && !ReferenceEquals(window, owner) && !ReferenceEquals(window.Owner, owner))
        {
            window.Owner = owner;
        }

        if (owner != null && ShouldCenterOnOwner(window, owner))
        {
            CenterOnOwner(window, owner);
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private static bool ShouldCenterOnOwner(Window window, Window owner)
    {
        if (double.IsNaN(window.Left) || double.IsNaN(window.Top))
        {
            return true;
        }

        if (Math.Abs(window.Left) > 0.5 || Math.Abs(window.Top) > 0.5)
        {
            return false;
        }

        Rect ownerBounds = GetOwnerBounds(owner);
        Size targetSize = GetTargetSize(window);
        Point windowCenter = new Point(window.Left + (targetSize.Width / 2), window.Top + (targetSize.Height / 2));

        return !ownerBounds.Contains(windowCenter);
    }

    private static void CenterOnOwner(Window window, Window owner)
    {
        Rect ownerBounds = GetOwnerBounds(owner);
        Size targetSize = GetTargetSize(window);

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = ownerBounds.Left + Math.Max(0, (ownerBounds.Width - targetSize.Width) / 2);
        window.Top = ownerBounds.Top + Math.Max(0, (ownerBounds.Height - targetSize.Height) / 2);
    }

    private static Rect GetOwnerBounds(Window owner)
    {
        return owner.WindowState == WindowState.Normal
            ? new Rect(owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight)
            : owner.RestoreBounds;
    }

    private static Size GetTargetSize(Window window)
    {
        double targetWidth = !double.IsNaN(window.Width) && window.Width > 0 ? window.Width : window.ActualWidth;
        double targetHeight = !double.IsNaN(window.Height) && window.Height > 0 ? window.Height : window.ActualHeight;

        if (targetWidth <= 0)
        {
            targetWidth = 600;
        }

        if (targetHeight <= 0)
        {
            targetHeight = 400;
        }

        return new Size(targetWidth, targetHeight);
    }
}
