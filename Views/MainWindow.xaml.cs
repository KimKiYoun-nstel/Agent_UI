using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Runtime.Versioning;
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
                // Traffic 컬렉션(메시지 탭)도 변경 시 자동 스크롤하도록 구독
                if (_vm.Traffic is INotifyCollectionChanged inccT)
                {
                    inccT.CollectionChanged += Traffic_CollectionChanged;
                }
                // BrowseConfig 동작은 View에서 FolderBrowserDialog를 사용하도록 훅을 설정
                try
                {
                    if (System.OperatingSystem.IsWindows())
                        _vm.BrowseConfigAction = ShowBrowseConfigDialog;
                }
                catch { }
            }
            catch { }
        }

    [SupportedOSPlatform("windows")]
    private void ShowBrowseConfigDialog()
        {
            try
            {
                if (_vm == null) return;
                using var dlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select your config directory",
                    SelectedPath = _vm.ConfigRoot
                };

                var res = dlg.ShowDialog();
                if (res == System.Windows.Forms.DialogResult.OK || res == System.Windows.Forms.DialogResult.Yes)
                {
                    _vm.ApplyConfigRoot(dlg.SelectedPath);
                }
            }
            catch { }
        }

        private void Logs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(RenderLogs));
        }

        private void Traffic_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // UI 스레드로 스크롤 처리
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var scroll = FindName("TrafficScroll") as ScrollViewer;
                    if (scroll != null)
                    {
                        // ScrollViewer에 아이템이 추가되면 맨 아래로 이동
                        scroll.ScrollToEnd();
                        return;
                    }

                    // ScrollViewer가 없을 경우 ItemsControl의 마지막 아이템을 BringIntoView
                    var items = FindName("TrafficItems") as ItemsControl;
                    if (items != null && items.Items.Count > 0)
                    {
                        var lastIdx = items.Items.Count - 1;
                        var container = items.ItemContainerGenerator.ContainerFromIndex(lastIdx) as FrameworkElement;
                        container?.BringIntoView();
                    }
                }
                catch { }
            }));
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
