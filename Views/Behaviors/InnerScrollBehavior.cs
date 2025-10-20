using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Agent.UI.Wpf.Views.Behaviors
{
    /// <summary>
    /// InnerTextBox에서 마우스휠 스크롤이 부모 ListBox의 항목 전환을 트리거하지 않도록 하는 Behavior
    /// TextBox 내부에서만 스크롤되도록 이벤트를 핸들링합니다.
    /// </summary>
    public static class InnerScrollBehavior
    {
        public static readonly DependencyProperty EnableInnerScrollProperty = DependencyProperty.RegisterAttached(
            "EnableInnerScroll", typeof(bool), typeof(InnerScrollBehavior), new PropertyMetadata(false, OnEnableChanged));

        public static void SetEnableInnerScroll(DependencyObject element, bool value) => element.SetValue(EnableInnerScrollProperty, value);
        public static bool GetEnableInnerScroll(DependencyObject element) => (bool)element.GetValue(EnableInnerScrollProperty);

        private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement el)
            {
                if ((bool)e.NewValue)
                {
                    el.PreviewMouseWheel += El_PreviewMouseWheel;
                }
                else
                {
                    el.PreviewMouseWheel -= El_PreviewMouseWheel;
                }
            }
        }

        private static void El_PreviewMouseWheel(object? sender, MouseWheelEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb)
            {
                // Try to get internal ScrollViewer (fallback to visual tree search)
                System.Windows.Controls.ScrollViewer? scrollViewer = null;
                try { scrollViewer = tb.Template.FindName("PART_ContentHost", tb) as System.Windows.Controls.ScrollViewer; } catch { }
                if (scrollViewer == null)
                {
                    try { scrollViewer = FindChildScrollViewer(tb); } catch { }
                }

                if (scrollViewer != null)
                {
                    var atTop = scrollViewer.VerticalOffset <= 0.0;
                    var atBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 0.1;

                    // Wheel up (positive delta) -> scroll up (decrease offset)
                    if (e.Delta > 0)
                    {
                        if (!atTop)
                        {
                            // consume and scroll inner
                            var newOff = Math.Max(0.0, scrollViewer.VerticalOffset - (e.Delta / 3.0));
                            scrollViewer.ScrollToVerticalOffset(newOff);
                            e.Handled = true;
                        }
                        else
                        {
                            // let parent handle (at top)
                            e.Handled = false;
                        }
                    }
                    else if (e.Delta < 0)
                    {
                        // Wheel down -> scroll down (increase offset)
                        if (!atBottom)
                        {
                            var newOff = Math.Min(scrollViewer.ScrollableHeight, scrollViewer.VerticalOffset - (e.Delta / 3.0));
                            scrollViewer.ScrollToVerticalOffset(newOff);
                            e.Handled = true;
                        }
                        else
                        {
                            // let parent handle (at bottom)
                            e.Handled = false;
                        }
                    }
                    else
                    {
                        e.Handled = true;
                    }
                }
                else
                {
                    // no internal scroll viewer -> swallow to avoid parent scrolling
                    e.Handled = true;
                }
            }
        }

        private static ScrollViewer? FindChildScrollViewer(DependencyObject d)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (child is ScrollViewer sv) return sv;
                var res = FindChildScrollViewer(child);
                if (res != null) return res;
            }
            return null;
        }
    }
}
