using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WorktreeHelper;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();

    /// <summary>Binds the opposite of a flag, for the "enabled while not busy" case.</summary>
    public static readonly NotConverter Not = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>
/// Shows one thing or the other depending on whether a value is there: a program's icon when
/// it could be had, and the button's own mark when it could not.
/// </summary>
public sealed class NullToVisibilityConverter(bool visibleWhenNull) : IValueConverter
{
    public static readonly NullToVisibilityConverter WhenSet = new(false);
    public static readonly NullToVisibilityConverter WhenNull = new(true);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is null) == visibleWhenNull ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Inverts a bool, for bindings that want the opposite of what they are given.</summary>
public sealed class NotConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
