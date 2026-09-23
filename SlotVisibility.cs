using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WorktreeHelper;

/// <summary>
/// Whether a row's button is drawn, keeps its place empty, or takes no room at all.
/// </summary>
/// <remarks>
/// The values, in order:
///   0  the row wants this button
///   1  the column is being kept open down the list - buttons in columns, and some row has one
///   2  (optional) another button that shares this slot is up in this row, so this one must
///      not hold the space as well
/// Held space is what lines a column up: a hidden button is laid out exactly like a shown
/// one, and every button in a row is the same width, so the next one lands where it would
/// in a row that has this one.
/// </remarks>
public sealed class SlotVisibility : IMultiValueConverter
{
    public static readonly SlotVisibility Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length > 0 && values[0] is true) return Visibility.Visible;

        var holdColumn = values.Length > 1 && values[1] is true;
        var slotTaken = values.Length > 2 && values[2] is true;
        return holdColumn && !slotTaken ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
