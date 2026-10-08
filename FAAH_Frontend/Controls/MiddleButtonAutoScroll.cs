using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace FAAH_Frontend.Controls;

public static class MiddleButtonAutoScroll
{
    private static readonly Dictionary<Window, AutoScrollSession> Sessions = new();
    private static readonly DispatcherTimer ScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private const double ScrollSensitivity = 0.11;
    private const double MaximumScrollSpeed = 70;

    static MiddleButtonAutoScroll()
    {
        ScrollTimer.Tick += OnScrollTimerTick;
    }

    public static void Attach(Window window)
    {
        window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        window.Closed += (_, _) => Stop(window);
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Window window || !e.GetCurrentPoint(window).Properties.IsMiddleButtonPressed)
            return;

        if (Sessions.ContainsKey(window))
        {
            Stop(window);
            e.Handled = true;
            return;
        }

        var scrollViewer = FindScrollViewer(window.InputHitTest(e.GetPosition(window)) as Visual);
        if (scrollViewer is null || scrollViewer.Extent.Height <= scrollViewer.Viewport.Height)
            return;

        if (window.Content is not Panel root)
            return;

        var overlay = new Canvas { IsHitTestVisible = false };
        Grid.SetRow(overlay, 0);
        Grid.SetRowSpan(overlay, 2);
        root.Children.Add(overlay);

        var upArrow = new TextBlock
        {
            Text = "▲",
            FontSize = 11,
            Foreground = Avalonia.Media.Brushes.White,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        var downArrow = new TextBlock
        {
            Text = "▼",
            FontSize = 11,
            Foreground = Avalonia.Media.Brushes.White,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        var marker = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(21),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E6263442")),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFF0B34A")),
            BorderThickness = new Thickness(2),
            Child = new StackPanel
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Spacing = -5,
                Children = { upArrow, downArrow }
            }
        };
        overlay.Children.Add(marker);
        var anchor = e.GetPosition(window);
        Canvas.SetLeft(marker, anchor.X - marker.Width / 2);
        Canvas.SetTop(marker, anchor.Y - marker.Height / 2);

        Sessions[window] = new AutoScrollSession(scrollViewer, anchor, overlay, marker, upArrow, downArrow);
        ScrollTimer.Start();
        e.Handled = true;
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Window window || !Sessions.TryGetValue(window, out var session))
            return;

        session.PointerPosition = e.GetPosition(window);
        var verticalDistance = session.PointerPosition.Y - session.AnchorPosition.Y;
        session.UpArrow.Opacity = verticalDistance < -8 ? 1 : 0.4;
        session.DownArrow.Opacity = verticalDistance > 8 ? 1 : 0.4;
        e.Handled = true;
    }

    private static void OnScrollTimerTick(object? sender, EventArgs e)
    {
        foreach (var session in Sessions.Values.ToArray())
        {
            var distance = session.PointerPosition.Y - session.AnchorPosition.Y;
            if (Math.Abs(distance) < 8)
                continue;

            var maxOffset = Math.Max(0, session.ScrollViewer.Extent.Height - session.ScrollViewer.Viewport.Height);
            var speed = Math.Sign(distance) * Math.Min(MaximumScrollSpeed, Math.Pow(Math.Abs(distance) - 8, 1.35) * ScrollSensitivity);
            var nextOffset = Math.Clamp(session.ScrollViewer.Offset.Y + speed, 0, maxOffset);
            session.ScrollViewer.Offset = new Vector(session.ScrollViewer.Offset.X, nextOffset);

        }
    }

    private static ScrollViewer? FindScrollViewer(Visual? visual)
    {
        while (visual is not null)
        {
            if (visual is ScrollViewer scrollViewer)
                return scrollViewer;

            visual = visual.GetVisualParent();
        }

        return null;
    }

    private static void Stop(Window window)
    {
        if (!Sessions.Remove(window, out var session))
            return;

        if (Sessions.Count == 0)
            ScrollTimer.Stop();

        session.Overlay.Children.Remove(session.Marker);
        if (session.Overlay.Parent is Panel parent)
            parent.Children.Remove(session.Overlay);

    }
    private sealed class AutoScrollSession(ScrollViewer scrollViewer, Point anchorPosition, Canvas overlay,
        Border marker, TextBlock upArrow, TextBlock downArrow)
    {
        public ScrollViewer ScrollViewer { get; } = scrollViewer;
        public Point AnchorPosition { get; } = anchorPosition;
        public Point PointerPosition { get; set; } = anchorPosition;
        public Canvas Overlay { get; } = overlay;
        public Border Marker { get; } = marker;
        public TextBlock UpArrow { get; } = upArrow;
        public TextBlock DownArrow { get; } = downArrow;
    }
}
