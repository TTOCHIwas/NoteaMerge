using System;
using System.Globalization;
using System.Windows.Data;

namespace Notea.Converters
{
    public class DynamicProgressToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 &&
                values[0] is double progress &&
                values[1] is double containerWidth)
            {
                // Progress는 0-1 사이의 값, containerWidth는 컨테이너의 실제 너비
                var result = Math.Max(0, Math.Min(1, progress)) * containerWidth;
                return result;
            }

            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}