using System.Globalization;
using System.Windows.Data;

namespace LinkerPlayer.Converters;

/// <summary>
/// Clamps a Double to a minimum of 0. Used to prevent the WPF DataGrid
/// CellsPanelHorizontalOffset binding from producing a negative Width on the
/// internal filler button in DataGridColumnHeadersPresenter, which causes the
/// "shimmy" / layout-thrash when scrolling horizontally.
/// </summary>
[ValueConversion(typeof(double), typeof(double))]
public class NonNegativeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d)
            return Math.Max(0d, d);
        return 0d;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
