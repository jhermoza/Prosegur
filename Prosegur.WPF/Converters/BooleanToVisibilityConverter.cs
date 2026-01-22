using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Prosegur.WPF.Converters;

/// <summary>
/// Converts boolean values to Visibility enum values.
/// Used to show/hide UI elements based on ViewModel state.
/// 
/// Design Pattern: Value Converter
/// This is a standard WPF pattern for transforming data during binding.
/// </summary>
public class BooleanToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Converts bool to Visibility.
    /// true -> Visible
    /// false -> Collapsed
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    /// <summary>
    /// Converts Visibility back to bool (rarely used).
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Visible;
        }

        return false;
    }
}
