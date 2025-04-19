using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Controls;

namespace LinkerPlayer.Converters
{
    public class IndexConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            DataGrid? dataGrid = values[0] as DataGrid;

            if (dataGrid != null && values[1] is { } item)
            {
                int index = dataGrid.Items.IndexOf(item) + 1;
                return index.ToString();  // Ensure it's a string
            }
            return string.Empty; // Return empty string instead of 0
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}