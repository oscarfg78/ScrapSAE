using System;
using System.Globalization;
using System.Windows.Data;

namespace ScrapSAE.Desktop.Infrastructure;

public sealed class NegateBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool flag ? !flag : true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool flag ? !flag : false;
    }
}
