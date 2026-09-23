using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VMDesk.Core.Enums;
using VMDesk.Core.Entities;
using WMedia = System.Windows.Media;

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

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ConnectionStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ConnectionState state)
            return new SolidColorBrush(Colors.Gray);

        return state switch
        {
            ConnectionState.Connected => new SolidColorBrush(WMedia.Color.FromRgb(0x4C, 0xAF, 0x50)),  // Success
            ConnectionState.Connecting => new SolidColorBrush(WMedia.Color.FromRgb(0xFF, 0xB3, 0x00)), // Warning
            ConnectionState.Reconnecting => new SolidColorBrush(WMedia.Color.FromRgb(0xFF, 0xB3, 0x00)), // Warning
            ConnectionState.Failed => new SolidColorBrush(WMedia.Color.FromRgb(0xEF, 0x53, 0x50)),      // Error
            ConnectionState.Disconnected => new SolidColorBrush(WMedia.Color.FromRgb(0x9E, 0x9E, 0x9E)), // Muted
            _ => new SolidColorBrush(WMedia.Color.FromRgb(0x9E, 0x9E, 0x9E))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ConnectionStateToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ConnectionState state) return "Unknown";

        return state switch
        {
            ConnectionState.Connected => "Connected",
            ConnectionState.Connecting => "Connecting...",
            ConnectionState.Reconnecting => "Reconnecting...",
            ConnectionState.Failed => "Connection Failed",
            ConnectionState.Disconnected => "Disconnected",
            _ => "Unknown"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ConnectionStateToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ConnectionState state) return Visibility.Collapsed;
        var targetState = parameter?.ToString();

        return state.ToString().Equals(targetState, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToFavoriteTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "Remove from favorites" : "Add to favorites";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (value is long lcount)
        {
            return lcount == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NotNullToVisibilityConverter : IValueConverter
{
    /// <summary>Shows the element only when the bound value is present (e.g. a nullable timestamp).</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is null || value == DependencyProperty.UnsetValue
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class CountToVisibilityInverseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (value is long lcount)
        {
            return lcount > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Shows the target while the bound string carries text; used for the discovery-note banner,
/// which must be invisible before the first scan and whenever no note applies (Task 7).
/// </summary>
public sealed class StringNonEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string text && !string.IsNullOrWhiteSpace(text) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}