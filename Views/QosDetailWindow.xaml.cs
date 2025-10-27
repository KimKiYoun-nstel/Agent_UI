using System.Windows;

namespace Agent.UI.Wpf.Views
{
    public partial class QosDetailWindow : Window
    {
        /// <summary>
        /// QoS 상세 창
        /// </summary>
        /// <param name="qosName">표시할 QoS 프로파일 이름</param>
        /// <param name="json">프로파일의 pretty-printed JSON 문자열</param>
        public QosDetailWindow(string qosName, string json)
        {
            InitializeComponent();
            // 창 타이틀과 상단 텍스트에 QoS 이름을 표시합니다.
            this.Title = $"QoS Detail - {qosName}";
            try { TitleText.Text = qosName; } catch { }
            DetailText.Text = json;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
