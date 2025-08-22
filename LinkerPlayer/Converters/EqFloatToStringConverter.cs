using System;
using System.Globalization;
using System.Windows.Data;

namespace LinkerPlayer.Converters
{
    public class EqFloatToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is float floatValue)
            {
                return floatValue > 0 ? floatValue.ToString("+0.0") : floatValue.ToString("0.0");
            }
            return "0.0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue && float.TryParse(stringValue, out float result))
            {
                return result;
            }
            return 0f;
        }
    }
}