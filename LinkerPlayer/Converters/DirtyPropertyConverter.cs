using System.Globalization;
using System.Windows.Data;
using LinkerPlayer.Models;

namespace LinkerPlayer.Converters;

/// <summary>
/// Converter that checks if a specific property is dirty on a MediaFile.
/// Returns true if the property is in the DirtyProperties set.
/// The property name is provided at construction time.
/// </summary>
public class DirtyPropertyConverter : IValueConverter
{
    private readonly string _propertyName;

    public DirtyPropertyConverter(string propertyName)
    {
        _propertyName = propertyName;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is MediaFile mediaFile)
        {
            return mediaFile.IsPropertyDirty(_propertyName);
        }

        // value might be DirtyCount (int) — can't resolve from here alone.
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
