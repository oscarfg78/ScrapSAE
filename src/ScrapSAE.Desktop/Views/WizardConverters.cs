using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ScrapSAE.Desktop.Views;

/// <summary>
/// Converts bool to Visibility: true → Visible, false → Collapsed
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>
/// Converts bool to Visibility (inverted): false → Visible, true → Collapsed
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}

/// <summary>
/// Converts bool: true → false, false → true
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>
/// Converts null reference to Collapsed, non-null to Visible
/// </summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Converts non-empty string to Visible, empty/null to Collapsed
/// </summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Converts int count > 0 to Visible, 0 to Collapsed
/// </summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Step color converter: given (isCompleted, isActive) returns the appropriate brush
/// </summary>
public sealed class StepColorConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush DoneBrush = new(Color.FromRgb(22, 163, 74));       // green
    private static readonly SolidColorBrush ActiveBrush = new(Color.FromRgb(37, 99, 235));     // blue
    private static readonly SolidColorBrush PendingBrush = new(Color.FromRgb(209, 213, 219));  // gray

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        bool isCompleted = values.Length > 0 && values[0] is true;
        bool isActive = values.Length > 1 && values[1] is true;

        if (isCompleted) return DoneBrush;
        if (isActive) return ActiveBrush;
        return PendingBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Coverage percentage to brush: ≥80 → green, ≥40 → yellow, else red
/// </summary>
public sealed class CoverageToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(22, 163, 74));
    private static readonly SolidColorBrush YellowBrush = new(Color.FromRgb(217, 119, 6));
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(220, 38, 38));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int pct)
        {
            if (pct >= 80) return GreenBrush;
            if (pct >= 40) return YellowBrush;
            return RedBrush;
        }
        return RedBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
