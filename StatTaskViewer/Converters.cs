using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace StatTaskViewer
{
    [System.Windows.Markup.MarkupExtensionReturnType(typeof(IValueConverter))]
    public class VisbilityToBooleanConverter :  IValueConverter
    {
        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return (Visibility)value == Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return (bool)value ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion
    }

    public class CheckForNull : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
}
