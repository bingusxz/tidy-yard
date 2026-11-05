using System.Globalization;
using System.Windows.Data;

namespace TidyYard.Views;

/// <summary>true -> false and back. Used to stop a swatch re-opening its own popup on the closing click.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
