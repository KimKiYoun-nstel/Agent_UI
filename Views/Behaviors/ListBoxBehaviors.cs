using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace Agent.UI.Wpf.Views.Behaviors
{
    /// <summary>
    /// ListBox용 Attached Behavior: ItemsSource 변경 시 자동으로 마지막 항목으로 스크롤합니다.
    /// UI code-behind 대신 XAML에서 사용하도록 제공합니다.
    /// </summary>
    public static class ListBoxBehaviors
    {
        public static readonly DependencyProperty AutoScrollProperty = DependencyProperty.RegisterAttached(
            "AutoScroll",
            typeof(bool),
            typeof(ListBoxBehaviors),
            new PropertyMetadata(false, OnAutoScrollChanged));

        public static void SetAutoScroll(DependencyObject element, bool value) => element.SetValue(AutoScrollProperty, value);
        public static bool GetAutoScroll(DependencyObject element) => (bool)element.GetValue(AutoScrollProperty);

        // Inner mouse wheel handling: when enabled on a ListBox, wheel events over inner TextBox
        // will scroll the TextBox content instead of moving ListBox selection.
        public static readonly DependencyProperty InnerMouseWheelProperty = DependencyProperty.RegisterAttached(
            "InnerMouseWheel",
            typeof(bool),
            typeof(ListBoxBehaviors),
            new PropertyMetadata(false, OnInnerMouseWheelChanged));

        public static void SetInnerMouseWheel(DependencyObject element, bool value) => element.SetValue(InnerMouseWheelProperty, value);
        public static bool GetInnerMouseWheel(DependencyObject element) => (bool)element.GetValue(InnerMouseWheelProperty);

        private static void OnInnerMouseWheelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is System.Windows.Controls.ListBox lb)
            {
                if ((bool)e.NewValue)
                    lb.PreviewMouseWheel += ListBox_PreviewMouseWheel;
                else
                    lb.PreviewMouseWheel -= ListBox_PreviewMouseWheel;
            }
        }

        private static void ListBox_PreviewMouseWheel(object? sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBox lb)
            {
                // Do a hit test at current mouse position to find the visual under the pointer
                var pos = System.Windows.Input.Mouse.GetPosition(lb);
                var hit = System.Windows.Media.VisualTreeHelper.HitTest(lb, pos)?.VisualHit as System.Windows.DependencyObject;
                var src = hit ?? (e.OriginalSource as System.Windows.DependencyObject);
                var tb = FindAncestor<System.Windows.Controls.TextBox>(src);
                if (tb != null)
                {
                    // find internal ScrollViewer
                    var sv = FindChildScrollViewer(tb);
                    if (sv != null)
                    {
                        var atTop = sv.VerticalOffset <= 0.0;
                        var atBottom = sv.VerticalOffset >= sv.ScrollableHeight - 0.1;
                        if (e.Delta > 0)
                        {
                            if (!atTop)
                            {
                                sv.ScrollToVerticalOffset(Math.Max(0.0, sv.VerticalOffset - (e.Delta / 3.0)));
                                e.Handled = true;
                            }
                        }
                        else if (e.Delta < 0)
                        {
                            if (!atBottom)
                            {
                                sv.ScrollToVerticalOffset(Math.Min(sv.ScrollableHeight, sv.VerticalOffset - (e.Delta / 3.0)));
                                e.Handled = true;
                            }
                        }
                    }
                }
            }
        }

        private static T? FindAncestor<T>(System.Windows.DependencyObject? d) where T : System.Windows.DependencyObject
        {
            while (d != null)
            {
                if (d is T t) return t;
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        private static System.Windows.Controls.ScrollViewer? FindChildScrollViewer(System.Windows.DependencyObject d)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (child is System.Windows.Controls.ScrollViewer sv) return sv;
                var res = FindChildScrollViewer(child);
                if (res != null) return res;
            }
            return null;
        }

        private static void OnAutoScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is System.Windows.Controls.ListBox lb)
            {
                if ((bool)e.NewValue)
                {
                    if (lb.ItemsSource is INotifyCollectionChanged nc)
                    {
                        nc.CollectionChanged += (s, ev) =>
                        {
                            if (ev.Action == NotifyCollectionChangedAction.Add)
                            {
                                if (lb.Items.Count > 0)
                                {
                                    var last = lb.Items[lb.Items.Count - 1];
                                    lb.Dispatcher.BeginInvoke(new System.Action(() => lb.ScrollIntoView(last)));
                                }
                            }
                            else if (ev.Action == NotifyCollectionChangedAction.Reset)
                            {
                                lb.Dispatcher.BeginInvoke(new System.Action(() => lb.Items.Refresh()));
                            }
                        };
                    }
                }
            }
        }
    }
}
