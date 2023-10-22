using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LinkerPlayer.Converters;

public class NullVisibilityConverter : IValueConverter
{
    public Visibility True { get; set; }

    public Visibility False { get; set; }

    public bool Invert { get; set; }

    public NullVisibilityConverter()
    {
        True = Visibility.Collapsed;
        False = Visibility.Visible;
    }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        return Invert ? (value == null ? False : True) : (value == null ? True : False);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return DependencyProperty.UnsetValue;
    }
}