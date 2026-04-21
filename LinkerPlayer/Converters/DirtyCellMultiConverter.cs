using System.Globalization;
using System.Windows.Data;
using LinkerPlayer.Models;

namespace LinkerPlayer.Converters;

/// <summary>
/// Multi-value converter that checks if a specific property is dirty on a MediaFile.
/// Bindings: [0] = DirtyCount (int, triggers re-evaluation), [1] = MediaFile (the DataContext).
/// Returns true if the specified property is in the DirtyProperties set.
/// </summary>
public class DirtyCellMultiConverter : IMultiValueConverter
{
    private readonly string _propertyName;

    public DirtyCellMultiConverter(string propertyName)
    {
        _propertyName = propertyName;
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[1] is MediaFile mediaFile)
        {
            return mediaFile.IsPropertyDirty(_propertyName);
        }

        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
