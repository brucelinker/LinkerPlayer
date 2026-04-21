using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LinkerPlayer.Models;

namespace LinkerPlayer.Converters;

/// <summary>
/// Multi-value converter that returns a light green brush when the cell's bound property is dirty.
/// Values[0] = MediaFile (DataContext of the row)
/// Values[1] = IsDirty (triggers re-evaluation when dirty state changes)
/// ConverterParameter = property name (string)
/// </summary>
public class DirtyCellBackgroundConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush DirtyBrush = new(Color.FromArgb(60, 0, 180, 0));
    private static readonly SolidColorBrush TransparentBrush = Brushes.Transparent;

    static DirtyCellBackgroundConverter()
    {
        DirtyBrush.Freeze();
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 &&
            values[0] is MediaFile mediaFile &&
            parameter is string propertyName &&
            mediaFile.IsPropertyDirty(propertyName))
        {
            return DirtyBrush;
        }

        return TransparentBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
