using System.Windows;

namespace Agent.UI.Wpf.Views
{
    public partial class QosDetailWindow : Window
    {
        public QosDetailWindow(string json)
        {
            InitializeComponent();
            DetailText.Text = json;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
