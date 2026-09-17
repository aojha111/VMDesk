using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VMDesk.App.Converters;

public sealed class ViewModeVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isList = value is true;
        var wantsList = string.Equals(parameter?.ToString(), "List", StringComparison.OrdinalIgnoreCase);
        return isList == wantsList ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}