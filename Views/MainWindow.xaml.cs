using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Collections.Specialized;
using Agent.UI.Wpf.ViewModels;

namespace Agent.UI.Wpf.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel? _vm;

        public MainWindow()
        {
            // Try to call generated InitializeComponent if present; otherwise fall back to loading XAML
            try
            {
                var mi = this.GetType().GetMethod("InitializeComponent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (mi != null) mi.Invoke(this, null);
                else
                {
                    var uri = new Uri("/Agent.UI.Wpf;component/views/mainwindow.xaml", UriKind.Relative);
                    System.Windows.Application.LoadComponent(this, uri);
                }
            }
            catch { }
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            TryWireLogs();
        }

        private void TryWireLogs()
        {
            try
            {
                _vm = DataContext as MainViewModel;
                if (_vm == null) return;

                RenderLogs();

                if (_vm.Logs is INotifyCollectionChanged incc)
                {
                    incc.CollectionChanged += Logs_CollectionChanged;
                }
            }
            catch { }
        }

        private void Logs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(RenderLogs));
        }

        private void RenderLogs()
        {
            try
            {
                if (_vm == null) return;
                var box = FindName("LogBox") as System.Windows.Controls.RichTextBox;
                if (box == null) return;

                var doc = new FlowDocument();
                foreach (var line in _vm.Logs)
                {
                    var p = new Paragraph { Margin = new Thickness(0) };
                    if (line.Contains(" IN ") || line.Contains(" OUT "))
                    {
                        var run = new Run(line) { Foreground = System.Windows.Media.Brushes.DodgerBlue };
                        p.Inlines.Add(run);
                    }
                    else
                    {
                        p.Inlines.Add(new Run(line));
                    }
                    doc.Blocks.Add(p);
                }

                box.Document = doc;
                box.ScrollToEnd();
            }
            catch { }
        }

        private void CopySelectedLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var box = FindName("LogBox") as System.Windows.Controls.RichTextBox;
                if (box == null) return;
                var txt = box.Selection.Text;
                if (!string.IsNullOrEmpty(txt))
                {
                    System.Windows.Clipboard.SetText(txt);
                    var prev = Title;
                    Title = "Logs copied";
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        await System.Threading.Tasks.Task.Delay(1200);
                        Dispatcher.Invoke(() => Title = prev);
                    });
                }
            }
            catch { }
        }
    }
}
