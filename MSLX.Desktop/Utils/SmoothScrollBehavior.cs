using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using System;

namespace MSLX.Desktop.Utils
{
    public class SmoothScrollBehavior
    {
        public static readonly AttachedProperty<bool> EnableSmoothScrollProperty =
            AvaloniaProperty.RegisterAttached<SmoothScrollBehavior, ScrollViewer, bool>("EnableSmoothScroll");

        public static bool GetEnableSmoothScroll(ScrollViewer element) => element.GetValue(EnableSmoothScrollProperty);
        public static void SetEnableSmoothScroll(ScrollViewer element, bool value) => element.SetValue(EnableSmoothScrollProperty, value);

        static SmoothScrollBehavior()
        {
            EnableSmoothScrollProperty.Changed.AddClassHandler<ScrollViewer>(OnEnableSmoothScrollChanged);
        }

        private static void OnEnableSmoothScrollChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                scrollViewer.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            }
            else
            {
                scrollViewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged);
            }
        }

        private static void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer) return;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

            e.Handled = true;

            var smoothScroller = GetOrCreateScroller(scrollViewer);
            smoothScroller.ScrollBy(-e.Delta.Y * 60); // 控制一次滚动的跨度的倍率
        }

        private static readonly AttachedProperty<SmoothScroller> ScrollerProperty =
            AvaloniaProperty.RegisterAttached<SmoothScrollBehavior, ScrollViewer, SmoothScroller>("Scroller");

        private static SmoothScroller GetOrCreateScroller(ScrollViewer scrollViewer)
        {
            var scroller = scrollViewer.GetValue(ScrollerProperty);
            if (scroller == null)
            {
                scroller = new SmoothScroller(scrollViewer);
                scrollViewer.SetValue(ScrollerProperty, scroller);
            }
            return scroller;
        }

        private class SmoothScroller
        {
            private readonly ScrollViewer _scrollViewer;
            private double _targetOffset;
            private DispatcherTimer? _timer;

            public SmoothScroller(ScrollViewer scrollViewer)
            {
                _scrollViewer = scrollViewer;
            }

            public void ScrollBy(double delta)
            {
                if (_timer == null || !_timer.IsEnabled)
                {
                    _targetOffset = _scrollViewer.Offset.Y;
                }

                _targetOffset += delta;
                
                var maxOffset = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
                _targetOffset = Math.Max(0, Math.Min(_targetOffset, maxOffset));

                if (_timer == null)
                {
                    _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnTick);
                }
                
                _timer.Start();
            }

            private void OnTick(object? sender, EventArgs e)
            {
                var currentOffset = _scrollViewer.Offset.Y;
                var diff = _targetOffset - currentOffset;

                // 阻尼系数，决定顺滑度和停止速度
                if (Math.Abs(diff) < 1.0)
                {
                    _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, _targetOffset);
                    _timer?.Stop();
                    return;
                }

                _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, currentOffset + diff * 0.25);
            }
        }
    }
}
