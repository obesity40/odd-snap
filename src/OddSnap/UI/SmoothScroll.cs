using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OddSnap.UI;

/// <summary>
/// Smooth, wheel-driven scrolling for a <see cref="ScrollViewer"/>.
/// </summary>
/// <remarks>
/// <para>
/// Only real mouse wheels are animated. Touchpad gestures arrive as a stream of
/// small wheel deltas and already have OS-level inertia, so animating them
/// makes the motion feel laggy; those are passed straight through to WPF.
/// </para>
/// <para>
/// While any ComboBox dropdown is open the page is left alone entirely. The
/// dropdown is a Popup anchored to its owner, so scrolling the page would
/// strand the open list; letting WPF handle the wheel keeps the list scrolling
/// itself and its anchor stationary.
/// </para>
/// <para>
/// Usage: <c>ui:SmoothScroll.IsEnabled="True"</c> on the ScrollViewer.
/// Respects <see cref="Motion.Disabled"/>.
/// </para>
/// </remarks>
internal static class SmoothScroll
{
    /// <summary>Pixels scrolled per mouse wheel notch.</summary>
    private const double PixelsPerNotch = 64d;

    /// <summary>
    /// Pixels per unit of touchpad delta. Touchpads report many small deltas per
    /// gesture, so this is small to keep movement proportional to the fingers
    /// instead of lurching ahead of them.
    /// </summary>
    private const double TouchpadPixelsPerDelta = 1.15d;

    /// <summary>
    /// Fraction of the remaining distance applied per frame. Higher follows the
    /// input more tightly. 0.6 lands within ~2 frames, which reads as immediate
    /// while still easing the final few pixels.
    /// </summary>
    private const double FollowPerFrame = 0.6d;

    /// <summary>Distance below which the glide snaps to the target and stops.</summary>
    private const double SettleThreshold = 0.5d;

    /// <summary>
    /// Deltas at or below this are treated as touchpad input. Touchpads emit a
    /// stream of small values; mice emit multiples of 120. Anything in between
    /// is left to WPF so trackpad inertia stays native.
    /// </summary>
    private const int PrecisionDeltaThreshold = 120;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SmoothScroll),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value)
        => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element)
        => (bool)element.GetValue(IsEnabledProperty);

    /// <summary>
    /// Where the glide is heading, accumulated from wheel deltas.
    /// NaN means "no glide in progress".
    /// </summary>
    private static readonly DependencyProperty TargetProperty =
        DependencyProperty.RegisterAttached(
            "Target",
            typeof(double),
            typeof(SmoothScroll),
            new PropertyMetadata(double.NaN));

    /// <summary>True while a rendering tick is subscribed for this viewer.</summary>
    private static readonly DependencyProperty IsTickingProperty =
        DependencyProperty.RegisterAttached(
            "IsTicking",
            typeof(bool),
            typeof(SmoothScroll),
            new PropertyMetadata(false));

    private static readonly System.Collections.Generic.HashSet<ScrollViewer> ActiveViewers = new();

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer)
            return;

        if ((bool)e.NewValue)
        {
            viewer.PreviewMouseWheel += OnPreviewMouseWheel;
            viewer.Loaded += OnLoaded;
            viewer.Unloaded += OnUnloaded;
            if (viewer.IsLoaded)
                OnLoaded(viewer, EventArgs.Empty);
        }
        else
        {
            viewer.PreviewMouseWheel -= OnPreviewMouseWheel;
            viewer.Loaded -= OnLoaded;
            viewer.Unloaded -= OnUnloaded;
            OnUnloaded(viewer, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Tracks which ComboBoxes inside <paramref name="sender"/> are open.
    /// DropDownOpened/Closed do not bubble, so they are attached directly to
    /// every ComboBox found in the viewer's tree.
    /// </summary>
    private static void OnLoaded(object sender, EventArgs e)
    {
        if (sender is not ScrollViewer viewer || _attachedViewers.Contains(viewer))
            return;

        _attachedViewers.Add(viewer);
        var open = new System.Collections.Generic.HashSet<System.Windows.Controls.ComboBox>();
        OpenDropDowns[viewer] = open;

        foreach (var combo in EnumerateComboBoxes(viewer))
        {
            void OnOpened(object? s, EventArgs args)
            {
                if (s is System.Windows.Controls.ComboBox c)
                    open.Add(c);
            }

            void OnClosed(object? s, EventArgs args)
            {
                if (s is System.Windows.Controls.ComboBox c)
                    open.Remove(c);
            }

            combo.DropDownOpened += OnOpened;
            combo.DropDownClosed += OnClosed;
            _comboHandlers[combo] = (OnOpened, OnClosed);

            if (combo.IsDropDownOpen)
                open.Add(combo);
        }
    }

    private static void OnUnloaded(object sender, EventArgs e)
    {
        if (sender is not ScrollViewer viewer || !_attachedViewers.Remove(viewer))
            return;

        OpenDropDowns.Remove(viewer);

        foreach (var combo in EnumerateComboBoxes(viewer))
        {
            if (_comboHandlers.TryGetValue(combo, out var handlers))
            {
                combo.DropDownOpened -= handlers.Opened;
                combo.DropDownClosed -= handlers.Closed;
                _comboHandlers.Remove(combo);
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<System.Windows.Controls.ComboBox> EnumerateComboBoxes(DependencyObject root)
    {
        var stack = new System.Collections.Generic.Stack<DependencyObject>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();

            if (node is System.Windows.Controls.ComboBox combo)
                yield return combo;

            if (node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D)
            {
                var count = VisualTreeHelper.GetChildrenCount(node);
                for (var i = 0; i < count; i++)
                    stack.Push(VisualTreeHelper.GetChild(node, i));
            }

            foreach (var child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is DependencyObject d)
                    stack.Push(d);
            }
        }
    }

    private static readonly System.Collections.Generic.HashSet<ScrollViewer> _attachedViewers = new();
    private static readonly System.Collections.Generic.Dictionary<ScrollViewer, System.Collections.Generic.HashSet<System.Windows.Controls.ComboBox>> OpenDropDowns = new();
    private static readonly System.Collections.Generic.Dictionary<
        System.Windows.Controls.ComboBox,
        (EventHandler Opened, EventHandler Closed)> _comboHandlers = new();

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer || e.Delta == 0)
            return;

        if (viewer.ScrollableHeight <= 0)
            return;

        // An open dropdown owns the wheel: its Popup would otherwise be left
        // behind while the page slides out from under it.
        if (IsAnyDropDownOpen(viewer))
            return;

        e.Handled = true;

        // Touchpads emit a stream of small deltas and are expected to track the
        // fingers 1:1; a mouse notch is a discrete step. Convert both to pixels
        // so a trackpad glide moves proportionally instead of jumping a notch.
        var isPrecision = Math.Abs(e.Delta) < PrecisionDeltaThreshold;

        if (Motion.Disabled)
        {
            var instant = Math.Clamp(
                viewer.VerticalOffset - DeltasToPixels(e.Delta, isPrecision), 0d, viewer.ScrollableHeight);
            viewer.ScrollToVerticalOffset(instant);
            viewer.SetValue(TargetProperty, instant);
            return;
        }

        // Continue from the in-flight target when a glide is already running so
        // consecutive deltas stack. The base value is only used when no glide
        // is active; reading the animated value would lag a frame behind.
        var target = viewer.GetValue(TargetProperty) is double pending && !double.IsNaN(pending)
            ? pending
            : viewer.VerticalOffset;

        // A stale target (viewer scrolled by other means, or the page changed
        // height) would yank the view backwards; drop it and start from here.
        if (Math.Abs(target - viewer.VerticalOffset) > viewer.ScrollableHeight)
            target = viewer.VerticalOffset;

        target = Math.Clamp(
            target - DeltasToPixels(e.Delta, isPrecision), 0d, viewer.ScrollableHeight);
        viewer.SetValue(TargetProperty, target);

        if (!(bool)viewer.GetValue(IsTickingProperty))
        {
            viewer.SetValue(IsTickingProperty, true);
            ActiveViewers.Add(viewer);
            if (ActiveViewers.Count == 1)
                CompositionTarget.Rendering += OnRendering;
        }
    }

    /// <summary>
    /// Converts a raw wheel delta into a pixel offset.
    /// </summary>
    /// <remarks>
    /// A mouse notch is a fixed step, so it maps to <see cref="PixelsPerNotch"/>.
    /// A touchpad reports many small deltas per gesture and expects proportional
    /// movement, so it is scaled by <see cref="TouchpadPixelsPerDelta"/> to keep
    /// the content under the fingers rather than lurching ahead of them.
    /// </remarks>
    private static double DeltasToPixels(int delta, bool isPrecision)
    {
        if (isPrecision)
            return delta * TouchpadPixelsPerDelta;

        var notches = delta / (double)Mouse.MouseWheelDeltaForOneLine;
        return notches * PixelsPerNotch;
    }

    /// <summary>
    /// True when the pointer is over a ComboBox that currently has its dropdown
    /// open, or over the dropdown itself.
    /// </summary>
    /// <remarks>
    /// The open/closed state comes from the tracked set maintained by
    /// <see cref="OnLoaded"/>, and the pointer position from
    /// <see cref="Mouse.DirectlyOver"/>. Combining the two avoids both the cost
    /// of walking the tree for ComboBoxes and the ambiguity of guessing from
    /// the event source: a Popup routes its wheel events from the host window,
    /// so the source alone cannot say whether the pointer is over the list.
    /// </remarks>
    private static bool IsAnyDropDownOpen(ScrollViewer viewer)
    {
        if (!OpenDropDowns.TryGetValue(viewer, out var open) || open.Count == 0)
            return false;

        var under = Mouse.DirectlyOver as DependencyObject;

        // Over the open list itself (its Popup is a separate window).
        if (under is not null && IsDescendantOfAnyPopup(under, open))
            return true;

        // Over the owning ComboBox.
        for (var current = under; current is not null; current = GetParent(current))
        {
            if (current is System.Windows.Controls.ComboBox combo && open.Contains(combo))
                return true;

            if (ReferenceEquals(current, viewer))
                break;
        }

        return false;
    }

    private static bool IsDescendantOfAnyPopup(DependencyObject node, System.Collections.Generic.HashSet<System.Windows.Controls.ComboBox> open)
    {
        for (var current = node; current is not null; current = GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.Popup popup &&
                popup.PlacementTarget is System.Windows.Controls.ComboBox owner &&
                open.Contains(owner))
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject node)
    {
        if (node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D)
        {
            var visualParent = System.Windows.Media.VisualTreeHelper.GetParent(node);
            if (visualParent is not null)
                return visualParent;
        }

        return LogicalTreeHelper.GetParent(node);
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        // One static handler serves every viewer that is mid-glide; the
        // rendered frame acts as the clock, so per-viewer timers are avoided.
        foreach (var viewer in System.Linq.Enumerable.ToArray(ActiveViewers))
        {
            if (!viewer.IsVisible || viewer.ScrollableHeight <= 0)
            {
                Stop(viewer);
                continue;
            }

            var target = (double)viewer.GetValue(TargetProperty);
            var current = viewer.VerticalOffset;
            var remaining = target - current;

            if (Math.Abs(remaining) <= SettleThreshold)
            {
                viewer.ScrollToVerticalOffset(target);
                Stop(viewer);
                continue;
            }

            viewer.ScrollToVerticalOffset(current + (remaining * FollowPerFrame));
        }
    }

    private static void Stop(ScrollViewer viewer)
    {
        viewer.SetValue(IsTickingProperty, false);
        // Drop the target with the glide: a stale value would be treated as the
        // starting point of the next gesture and yank the view backwards.
        viewer.SetValue(TargetProperty, double.NaN);
        ActiveViewers.Remove(viewer);
        if (ActiveViewers.Count == 0)
            CompositionTarget.Rendering -= OnRendering;
    }
}
