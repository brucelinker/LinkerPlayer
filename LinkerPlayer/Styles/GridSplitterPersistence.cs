using LinkerPlayer.Core;
using LinkerPlayer.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace LinkerPlayer;

public static class GridSplitterPersistence
{
    public static readonly DependencyProperty PersistKeyProperty = DependencyProperty.RegisterAttached(
        "PersistKey",
        typeof(string),
        typeof(GridSplitterPersistence),
        new PropertyMetadata(null, OnPersistKeyChanged));

    public static void SetPersistKey(DependencyObject element, string? value) => element.SetValue(PersistKeyProperty, value);
    public static string? GetPersistKey(DependencyObject element) => (string?)element.GetValue(PersistKeyProperty);

    private static void OnPersistKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        GridSplitter? splitter = d as GridSplitter;
        if (splitter == null)
        {
            return;
        }

        splitter.Loaded -= Splitter_Loaded;
        splitter.DragCompleted -= Splitter_DragCompleted;
        if (e.NewValue is string s && !string.IsNullOrWhiteSpace(s))
        {
            splitter.Loaded += Splitter_Loaded;
            splitter.DragCompleted += Splitter_DragCompleted;
        }
    }

    private static void Splitter_Loaded(object sender, RoutedEventArgs e)
    {
        GridSplitter splitter = (GridSplitter)sender;
        string? key = GetPersistKey(splitter);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        Grid? grid = splitter.Parent as Grid;
        if (grid == null)
        {
            return;
        }

        if (App.AppHost?.Services == null)
        {
            return; // AppHost not ready (e.g., design-time)
        }

        ISettingsManager settings;
        try
        {
            settings = App.AppHost.Services.GetRequiredService<ISettingsManager>();
        }
        catch
        {
            return;
        }

        if (!settings.Settings.SplitterLayouts.TryGetValue(key, out List<double>? ratios) || ratios == null || ratios.Count == 0)
        {
            return;
        }

        if (splitter.ResizeDirection == GridResizeDirection.Rows)
        {
            int j = 0;
            for (int i = 0; i < grid.RowDefinitions.Count && j < ratios.Count; i++)
            {
                if (grid.RowDefinitions[i].Height.IsStar)
                {
                    double part = ratios[j++];
                    if (part <= 0)
                    {
                        continue;
                    }

                    grid.RowDefinitions[i].Height = new GridLength(part, GridUnitType.Star);
                }
            }
        }
        else
        {
            int j = 0;
            for (int i = 0; i < grid.ColumnDefinitions.Count && j < ratios.Count; i++)
            {
                if (grid.ColumnDefinitions[i].Width.IsStar)
                {
                    double part = ratios[j++];
                    if (part <= 0)
                    {
                        continue;
                    }

                    grid.ColumnDefinitions[i].Width = new GridLength(part, GridUnitType.Star);
                }
            }
        }
    }

    private static void Splitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        GridSplitter splitter = (GridSplitter)sender;
        string? key = GetPersistKey(splitter);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        Grid? grid = splitter.Parent as Grid;
        if (grid == null)
        {
            return;
        }

        List<double> sizes;
        if (splitter.ResizeDirection == GridResizeDirection.Rows)
        {
            sizes = grid.RowDefinitions
                .Where(r => r.Height.IsStar)
                .Select(r => r.ActualHeight)
                .ToList();
        }
        else
        {
            sizes = grid.ColumnDefinitions
                .Where(c => c.Width.IsStar)
                .Select(c => c.ActualWidth)
                .ToList();
        }

        Normalize(sizes);

        if (App.AppHost?.Services == null)
        {
            return;
        }
        ISettingsManager settings;
        try
        {
            settings = App.AppHost.Services.GetRequiredService<ISettingsManager>();
        }
        catch
        {
            return;
        }

        if (settings.Settings.SplitterLayouts == null)
        {
            settings.Settings.SplitterLayouts = new Dictionary<string, List<double>>();
        }
        settings.Settings.SplitterLayouts[key] = sizes;
        settings.SaveSettings(nameof(AppSettings.SplitterLayouts));
    }

    private static void Normalize(List<double> values)
    {
        double sum = values.Sum();
        if (sum <= 0)
        {
            return;
        }

        for (int i = 0; i < values.Count; i++)
        {
            values[i] = values[i] / sum;
        }
    }
}
