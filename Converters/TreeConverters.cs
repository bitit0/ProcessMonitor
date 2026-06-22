using System.Globalization;
using System.Windows.Data;

namespace ProcessMonitor.Converters;

public class DepthToWidth : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int d ? d * 16.0 : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
