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
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private FlowDocument? _messagesDocument;

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                // create FlowDocument once
                _messagesDocument = new FlowDocument();
                _messagesDocument.FontFamily = new System.Windows.Media.FontFamily("Consolas");
                _messagesDocument.FontSize = 12;
                _messagesDocument.PagePadding = new Thickness(6);
                if (MessagesViewer != null) MessagesViewer.Document = _messagesDocument;

                // initial population
                foreach (var item in vm.Traffic) AppendTrafficItem(item);

                // subscribe to collection changes and append new items incrementally
                vm.Traffic.CollectionChanged += (s, ev) =>
                {
                    // only handle add/reset for simplicity
                    if (ev.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && ev.NewItems != null)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            foreach (var ni in ev.NewItems)
                            {
                                if (ni is Agent.UI.Wpf.ViewModels.MainViewModel.TrafficItem ti)
                                    AppendTrafficItem(ti);
                            }
                        }));
                    }
                    else if (ev.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                    {
                        // clear document
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            _messagesDocument?.Blocks.Clear();
                        }));
                    }
                };
            }
        }

        private void AppendTrafficItem(Agent.UI.Wpf.ViewModels.MainViewModel.TrafficItem item)
        {
            if (_messagesDocument == null) return;

            var header = new Paragraph();
            header.Margin = new Thickness(0, 0, 0, 2);
            var run = new Run(item.Header + "\n") { FontWeight = FontWeights.Bold };
            run.Foreground = item.IsInbound ? System.Windows.Media.Brushes.Blue : System.Windows.Media.Brushes.Black;
            header.Inlines.Add(run);
            _messagesDocument.Blocks.Add(header);

            var body = new Paragraph();
            body.Margin = new Thickness(0, 0, 0, 8);
            var bodyRun = new Run(item.Json + "\n");
            body.Inlines.Add(bodyRun);
            _messagesDocument.Blocks.Add(body);

            // keep document length bounded if needed (optional: trim older messages)
            // Example: keep only last 1000 blocks to avoid memory blowup
            const int maxBlocks = 2000;
            while (_messagesDocument.Blocks.Count > maxBlocks)
            {
                var first = _messagesDocument.Blocks.FirstBlock;
                if (first != null) _messagesDocument.Blocks.Remove(first);
                else break;
            }
        }
        
    }
}
