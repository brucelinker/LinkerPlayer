using System.Windows.Controls;
using System.Windows.Media;
using System.Windows;

namespace LinkerPlayer.Core;

public static class DataGridRowHelper
{
    public static int GetIndex(this DataGridRow row)
    {
        //var dataGrid = FindVisualParent(row);
        //if (dataGrid != null)
        //{
        //    return dataGrid.ItemContainerGenerator.IndexFromContainer(row);
        //}
        return -1; // Error case, no DataGrid found
    }

    //private static T FindVisualParent(DependencyObject child) where T : DependencyObject
    //{
    //    //var parentObject = VisualTreeHelper.GetParent(child);
    //    //if (parentObject == null) return null;
    //    //if (parentObject is T parent) return parent;
    //    return null; // FindVisualParent(parentObject);
    //}
}