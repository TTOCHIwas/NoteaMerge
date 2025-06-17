using System;
using System.Globalization;
using System.Windows.Data;

namespace Notea.Converters
{
    public class ProgressToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double containerWidth && parameter != null)
            {
                if (double.TryParse(parameter.ToString(), out double ratio))
                {
                    return containerWidth * ratio; // 예: 0.5면 50% 너비
                }
            }

            double progress = value is double ? (double)value : 0.0;
            double maxWidth = parameter != null ? double.Parse(parameter.ToString()) : 100.0; // 기본 최대 너비
            return progress * maxWidth; // 예: 0.7 * 100 = 70
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}