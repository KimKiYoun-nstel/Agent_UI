using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Agent.UI.Wpf.Views.Converters
{
    /// <summary>
    /// TrafficItem.IsInbound에 따라 파랑색(및 약간 밝은 회색)으로 Brush를 반환합니다.
    /// </summary>
    public class TrafficBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is bool inbound && inbound)
                {
                    return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x6A, 0xFF)); // 파랑 계열
                }
            }
            catch { }
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0x22, 0x22)); // 기본 텍스트 색상
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
