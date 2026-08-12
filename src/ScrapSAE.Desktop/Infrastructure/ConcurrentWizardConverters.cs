using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ScrapSAE.Desktop.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────────
// BoolToVisibilityConverter
// ─────────────────────────────────────────────────────────────────────────────

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

// ─────────────────────────────────────────────────────────────────────────────
// BoolToInverseVisibilityConverter
// ─────────────────────────────────────────────────────────────────────────────

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToInverseVisibilityConverter : IValueConverter
{
    public static readonly BoolToInverseVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

// ─────────────────────────────────────────────────────────────────────────────
// BoolToAccentConverter  (bool → accent brush or muted brush)
// ─────────────────────────────────────────────────────────────────────────────

[ValueConversion(typeof(bool), typeof(Brush))]
public class BoolToAccentConverter : IValueConverter
{
    public static readonly BoolToAccentConverter Instance = new();

    private static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(124, 58, 237));
    private static readonly SolidColorBrush MutedBrush  = new(Color.FromRgb(58, 58, 92));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? AccentBrush : MutedBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}

// ─────────────────────────────────────────────────────────────────────────────
// EnumToBoolConverter  (enum → bool for RadioButton IsChecked binding)
// ─────────────────────────────────────────────────────────────────────────────

public class EnumToBoolConverter : IValueConverter
{
    public static readonly EnumToBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is string param && value != null)
            return value.ToString() == param;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string param)
            return Enum.Parse(targetType, param);
        return Binding.DoNothing;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// EnumToVisibilityConverter (enum + parameter → Visibility)
// ─────────────────────────────────────────────────────────────────────────────

public class EnumToVisibilityConverter : IValueConverter
{
    public static readonly EnumToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString() ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}

// ─────────────────────────────────────────────────────────────────────────────
// EnumToAccentConverter (enum + parameter → accent/muted border color)
// ─────────────────────────────────────────────────────────────────────────────

public class EnumToAccentConverter : IValueConverter
{
    public static readonly EnumToAccentConverter Instance = new();

    private static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(109, 40, 217));
    private static readonly SolidColorBrush MutedBrush  = new(Color.FromRgb(42, 42, 62));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString() ? AccentBrush : MutedBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}

// ─────────────────────────────────────────────────────────────────────────────
// StatusToColorConverter (ConsolidatedStatus → background brush)
// ─────────────────────────────────────────────────────────────────────────────

public class StatusToColorConverter : IValueConverter
{
    public static readonly StatusToColorConverter Instance = new();

    private static readonly SolidColorBrush MatchedBrush    = new(Color.FromRgb(5, 150, 105));
    private static readonly SolidColorBrush NotMatchedBrush = new(Color.FromRgb(220, 38, 38));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == "Matched" ? MatchedBrush : NotMatchedBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
