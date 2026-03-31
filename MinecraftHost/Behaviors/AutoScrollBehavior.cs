using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MinecraftHost.Behaviors;

public static class AutoScrollBehavior
{
    public static readonly DependencyProperty AutoScrollProperty =
        DependencyProperty.RegisterAttached(
            "AutoScroll", typeof(bool), typeof(AutoScrollBehavior),
            new UIPropertyMetadata(false, AutoScrollPropertyChanged));

    public static bool GetAutoScroll(DependencyObject obj) => (bool)obj.GetValue(AutoScrollProperty);
    public static void SetAutoScroll(DependencyObject obj, bool value) => obj.SetValue(AutoScrollProperty, value);

    private static readonly DependencyProperty HookedScrollViewerProperty =
        DependencyProperty.RegisterAttached("HookedScrollViewer", typeof(ScrollViewer), typeof(AutoScrollBehavior));

    private static readonly DependencyProperty ScrollResumeTimerProperty =
        DependencyProperty.RegisterAttached("ScrollResumeTimer", typeof(System.Windows.Threading.DispatcherTimer), typeof(AutoScrollBehavior));

    private static readonly DependencyProperty IsUserScrollingProperty =
        DependencyProperty.RegisterAttached("IsUserScrolling", typeof(bool), typeof(AutoScrollBehavior), new PropertyMetadata(false));

    private static void AutoScrollPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        if ((bool)e.NewValue)
        {
            element.Loaded += ElementOnLoaded;
            element.Unloaded += ElementOnUnloaded;
            element.DataContextChanged += ElementOnDataContextChanged;
            AttachToChildScrollViewer(element);
        }
        else
        {
            element.Loaded -= ElementOnLoaded;
            element.Unloaded -= ElementOnUnloaded;
            element.DataContextChanged -= ElementOnDataContextChanged;
            DetachFromChildScrollViewer(element);
        }
    }

    private static void ElementOnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            AttachToChildScrollViewer(element);
    }

    private static void ElementOnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            DetachFromChildScrollViewer(element);
    }

    private static void ElementOnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement element && element.GetValue(HookedScrollViewerProperty) is ScrollViewer sv)
        {
            sv.SetValue(IsUserScrollingProperty, false);
            if (sv.GetValue(ScrollResumeTimerProperty) is System.Windows.Threading.DispatcherTimer timer)
            {
                timer.Stop();
            }

            ScrollToBottomReliably(sv);
        }
    }


    private static void AttachToChildScrollViewer(FrameworkElement element)
    {
        element.Dispatcher.InvokeAsync(() =>
        {
            var existing = (ScrollViewer?)element.GetValue(HookedScrollViewerProperty);
            if (existing is not null)
                existing.ScrollChanged -= ScrollViewer_ScrollChanged;

            var childScrollViewer = element as ScrollViewer ?? FindDescendant<ScrollViewer>(element);
            if (childScrollViewer is null)
            {
                element.ClearValue(HookedScrollViewerProperty);
                return;
            }

            childScrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
            childScrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            element.SetValue(HookedScrollViewerProperty, childScrollViewer);

            if (childScrollViewer.GetValue(ScrollResumeTimerProperty) is System.Windows.Threading.DispatcherTimer oldTimer)
            {
                oldTimer.Stop();
                oldTimer.Tick -= TimerOnTick;
            }

            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3),
                Tag = childScrollViewer
            };
            timer.Tick += TimerOnTick;
            childScrollViewer.SetValue(ScrollResumeTimerProperty, timer);
            childScrollViewer.SetValue(IsUserScrollingProperty, false);

            ScrollToBottomReliably(childScrollViewer);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static void DetachFromChildScrollViewer(FrameworkElement element)
    {
        var existing = (ScrollViewer?)element.GetValue(HookedScrollViewerProperty);
        if (existing is not null)
        {
            existing.ScrollChanged -= ScrollViewer_ScrollChanged;
            if (existing.GetValue(ScrollResumeTimerProperty) is System.Windows.Threading.DispatcherTimer timer)
            {
                timer.Stop();
                timer.Tick -= TimerOnTick;
            }
            existing.ClearValue(ScrollResumeTimerProperty);
            existing.ClearValue(IsUserScrollingProperty);
        }

        element.ClearValue(HookedScrollViewerProperty);
    }

    private static void TimerOnTick(object? sender, EventArgs e)
    {
        if (sender is System.Windows.Threading.DispatcherTimer timer && timer.Tag is ScrollViewer sv)
        {
            timer.Stop();
            sv.SetValue(IsUserScrollingProperty, false);
            ScrollToBottomReliably(sv);
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var childrenCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found)
                return found;

            var nested = FindDescendant<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv)
            return;

        if (e.ExtentHeightChange == 0)
        {
            bool isAtBottom = sv.VerticalOffset >= System.Math.Max(0, sv.ScrollableHeight - 1.0);

            if (!isAtBottom)
            {
                sv.SetValue(IsUserScrollingProperty, true);
                if (sv.GetValue(ScrollResumeTimerProperty) is System.Windows.Threading.DispatcherTimer timer)
                {
                    timer.Stop();
                    timer.Start();
                }
            }
            else
            {
                sv.SetValue(IsUserScrollingProperty, false);
                if (sv.GetValue(ScrollResumeTimerProperty) is System.Windows.Threading.DispatcherTimer timer)
                {
                    timer.Stop();
                }
            }
        }
        else
        {
            bool isUserScrolling = (bool)sv.GetValue(IsUserScrollingProperty);
            if (!isUserScrolling)
            {
                ScrollToBottomReliably(sv);
            }
        }
    }

    private static void ScrollToBottomReliably(ScrollViewer sv)
    {
        sv.Dispatcher.InvokeAsync(() =>
        {
            sv.UpdateLayout();
            sv.ScrollToBottom();

            sv.Dispatcher.InvokeAsync(() =>
            {
                sv.UpdateLayout();
                sv.ScrollToBottom();
            }, System.Windows.Threading.DispatcherPriority.ContextIdle);

        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public static readonly DependencyProperty ForceScrollTargetProperty =
        DependencyProperty.RegisterAttached(
            "ForceScrollTarget", typeof(DependencyObject), typeof(AutoScrollBehavior),
            new UIPropertyMetadata(null, ForceScrollTargetChanged));

    public static DependencyObject GetForceScrollTarget(DependencyObject obj) => (DependencyObject)obj.GetValue(ForceScrollTargetProperty);
    public static void SetForceScrollTarget(DependencyObject obj, DependencyObject value) => obj.SetValue(ForceScrollTargetProperty, value);

    private static void ForceScrollTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is System.Windows.Controls.Primitives.TextBoxBase tb)
        {
            tb.PreviewKeyDown -= Input_PreviewKeyDown;
            if (e.NewValue != null)
                tb.PreviewKeyDown += Input_PreviewKeyDown;
        }
        else if (d is System.Windows.Controls.Primitives.ButtonBase bb)
        {
            bb.Click -= Button_Click;
            if (e.NewValue != null)
                bb.Click += Button_Click;
        }
    }

    private static void Input_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && sender is DependencyObject d)
            TriggerForceScroll(d);
    }

    private static void Button_Click(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject d)
            TriggerForceScroll(d);
    }

    private static void TriggerForceScroll(DependencyObject source)
    {
        var target = GetForceScrollTarget(source);
        if (target != null)
        {
            var sv = target as ScrollViewer ?? (target as FrameworkElement)?.GetValue(HookedScrollViewerProperty) as ScrollViewer;
            if (sv == null && target is DependencyObject obj)
                sv = FindDescendant<ScrollViewer>(obj);

            if (sv != null)
            {
                sv.SetValue(IsUserScrollingProperty, false);
                if (sv.GetValue(ScrollResumeTimerProperty) is System.Windows.Threading.DispatcherTimer timer)
                    timer.Stop();
                ScrollToBottomReliably(sv);
            }
        }
    }
}