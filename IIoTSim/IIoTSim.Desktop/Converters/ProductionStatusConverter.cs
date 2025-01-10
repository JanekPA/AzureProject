using IIoTSim.Desktop.Model.Enums;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IIoTSim.Desktop.Converters
{
    public class ProductionStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch
            {
                ProductionStatus status => status == ProductionStatus.Running,
                _ => DependencyProperty.UnsetValue
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch
            {
                bool status => status ? ProductionStatus.Running : ProductionStatus.Stopped,
                _ => DependencyProperty.UnsetValue
            };
        }
    }
}