using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Prosegur.WPF.Converters;

public class StatusToColorConverter : IValueConverter
{
    private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(40, 167, 69));
    private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(220, 53, 69));
    private static readonly Brush OrangeBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7));
    private static readonly Brush GrayBrush = new SolidColorBrush(Color.FromRgb(108, 117, 125));
    private static readonly Brush BlackBrush = Brushes.Black;

    static StatusToColorConverter()
    {
        GreenBrush.Freeze();
        RedBrush.Freeze();
        OrangeBrush.Freeze();
        GrayBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string status)
        {
            return status.ToUpperInvariant() switch
            {
                "APPROVED" => GreenBrush,
                "DECLINED" => RedBrush,
                "FAILED" => RedBrush,
                "ERROR" => RedBrush,
                "PENDING" => OrangeBrush,
                "CANCELED" => GrayBrush,
                _ => BlackBrush
            };
        }

        return BlackBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
