using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Agent.UI.Wpf.Views.Converters
{
    /// <summary>
    /// 로그 문자열에 ' IN ' 또는 ' OUT ' 단어가 포함되어 있으면 파랑색으로, 아니면 기본 텍스트 색상으로 반환합니다.
    /// </summary>
    public class LogBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var s = value as string ?? string.Empty;
                if (s.Contains(" IN ") || s.Contains(" OUT "))
                    return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x6A, 0xFF));
            }
            catch { }
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0x22, 0x22));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
