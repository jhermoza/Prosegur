using System.Globalization;
using System.Windows.Data;

namespace Prosegur.WPF.Converters;

/// <summary>
/// Inverts a boolean value.
/// Used for scenarios where you need to show/hide opposite UI elements.
/// Example: Show "Process Payment" button when NOT processing.
/// </summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }

        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }

        return false;
    }
}
