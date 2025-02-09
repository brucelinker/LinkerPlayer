using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LinkerPlayer.Core;

internal static class Helper
{
    // Helper.FindVisualChildren<Grid>(this).FirstOrDefault()!.Focus();
    public static IEnumerable<T> FindVisualChildren<T>(DependencyObject? depObj) where T : DependencyObject
    {
        if (depObj != null)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);

                if (child is T dependencyObject)
                    yield return dependencyObject;

                foreach (T childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
            }
        }
    }

    public static int GetIndex(this DataGridRow row)
    {
        DataGrid? dataGrid = FindVisualParent<DataGrid>(row);

        if (dataGrid != null)
        {
            return dataGrid.ItemContainerGenerator.IndexFromContainer(row);
        }

        return -1; // Error case, no DataGrid found
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        while (true)
        {
            DependencyObject? parentObject = VisualTreeHelper.GetParent(child);

            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;

            child = parentObject;
        }
    }

    public static List<string> GetAllMp3Files(string[] files)
    {
        // all mp3 files including in directories and subdirectories
        List<string> mp3Files = new List<string>();

        foreach (var file in files)
        {
            if (File.Exists(file))
            {
                if (Path.GetExtension(file).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
                {
                    mp3Files.Add(file);
                }
            }
            else if (Directory.Exists(file))
            {
                string dir = file;

                foreach (string mp3File in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                             .Where(f => Path.GetExtension(f).Equals(".mp3", StringComparison.OrdinalIgnoreCase)))
                {
                    mp3Files.Add(mp3File);
                }
            }
        }

        return mp3Files;
    }
}