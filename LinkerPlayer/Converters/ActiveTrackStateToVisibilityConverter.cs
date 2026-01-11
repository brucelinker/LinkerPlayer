using ManagedBass;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LinkerPlayer.Converters;

public class ActiveTrackStateToVisibilityConverter : IMultiValueConverter
{
    public Visibility TrueValue { get; set; } = Visibility.Visible;
    public Visibility FalseValue { get; set; } = Visibility.Hidden;

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 3)
        {
            return this.FalseValue;
        }

        string? rowId = values[0] as string;
        string? activeId = values[1] as string;

        if (values[2] is not PlaybackState playbackState)
        {
            return this.FalseValue;
        }

        if (parameter is not PlaybackState desiredState)
        {
            return this.FalseValue;
        }

        if (string.IsNullOrWhiteSpace(rowId) || string.IsNullOrWhiteSpace(activeId))
        {
            return this.FalseValue;
        }

        bool isActive = string.Equals(rowId, activeId, StringComparison.Ordinal);
        bool isDesiredState = playbackState == desiredState;

        return (isActive && isDesiredState) ? this.TrueValue : this.FalseValue;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        return []; // one-way
    }
}
