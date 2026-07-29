using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.WPF.InstanceConsole.View.Dialogs;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MCServerLauncher.WPF.InstanceConsole.View.Pages
{
    public partial class EventTriggerPage
    {
        private readonly EventTriggerViewModel _viewModel;
        private Point _selectionStart;
        private Point _pendingSelectionStart;
        private bool _hasPendingBoxSelection;
        private bool _isBoxSelecting;
        private HashSet<EventRule> _initialSelection = [];
        private System.Windows.Window? _hostWindow;

        public EventTriggerPage()
        {
            InitializeComponent();
            _viewModel = App.Services.GetRequiredService<EventTriggerViewModel>();
            DataContext = _viewModel;
            Loaded += EventTriggerPage_Loaded;
            Loaded += (_, _) => AttachHostWindow();
            Unloaded += (_, _) => DetachHostWindow();
            IsVisibleChanged += (_, _) =>
            {
                if (!IsVisible)
                    CancelBoxSelection();
            };
        }

        private void AttachHostWindow()
        {
            _hostWindow = System.Windows.Window.GetWindow(this);
            if (_hostWindow is not null)
                _hostWindow.Deactivated += HostWindow_Deactivated;
        }

        private void DetachHostWindow()
        {
            if (_hostWindow is not null)
                _hostWindow.Deactivated -= HostWindow_Deactivated;
            _hostWindow = null;
        }

        private void HostWindow_Deactivated(object? sender, EventArgs e) => CancelBoxSelection();

        private async void EventTriggerPage_Loaded(object sender, RoutedEventArgs e)
        {
            await _viewModel.LoadRulesCommand.ExecuteAsync(null);
        }

        private void RulesListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pendingSelectionStart = e.GetPosition(RulesListView);
            _selectionStart = GetContentPoint(_pendingSelectionStart);
            _initialSelection = RulesListView.SelectedItems.Cast<EventRule>().ToHashSet();
            _hasPendingBoxSelection = true;

            if (FindVisualParent<ListViewItem>(e.OriginalSource as DependencyObject) is not null)
                return;

            BeginBoxSelection();
            UpdateBoxSelection(_pendingSelectionStart);
            e.Handled = true;
        }

        private void RulesListView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            var current = e.GetPosition(RulesListView);
            if (!_isBoxSelecting && _hasPendingBoxSelection)
            {
                var horizontalDistance = Math.Abs(current.X - _pendingSelectionStart.X);
                var verticalDistance = Math.Abs(current.Y - _pendingSelectionStart.Y);
                if (horizontalDistance < SystemParameters.MinimumHorizontalDragDistance &&
                    verticalDistance < SystemParameters.MinimumVerticalDragDistance)
                    return;

                BeginBoxSelection();
            }

            if (!_isBoxSelecting)
                return;

            ScrollForBoxSelection(current);
            UpdateBoxSelection(current);
            e.Handled = true;
        }

        private void BeginBoxSelection()
        {
            _selectionStart = GetContentPoint(_pendingSelectionStart);
            _hasPendingBoxSelection = false;
            _isBoxSelecting = true;
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                RulesListView.SelectedItems.Clear();
            RulesListView.CaptureMouse();
        }

        private void UpdateBoxSelection(Point viewportPoint)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(RulesListView);
            var horizontalOffset = scrollViewer?.HorizontalOffset ?? 0;
            var verticalOffset = scrollViewer?.VerticalOffset ?? 0;
            var current = GetContentPoint(viewportPoint);
            var contentSelection = new Rect(_selectionStart, current);
            var itemArea = GetItemAreaBounds();
            var viewportSelection = new Rect(
                new Point(contentSelection.Left - horizontalOffset, contentSelection.Top - verticalOffset),
                new Point(contentSelection.Right - horizontalOffset, contentSelection.Bottom - verticalOffset));
            if (itemArea is { } visibleItemArea)
                viewportSelection.Intersect(visibleItemArea);
            viewportSelection.Intersect(new Rect(0, 0, RulesListView.ActualWidth, RulesListView.ActualHeight));

            SelectionBox.Visibility = Visibility.Visible;
            SelectionBox.HorizontalAlignment = HorizontalAlignment.Left;
            SelectionBox.VerticalAlignment = VerticalAlignment.Top;
            SelectionBox.Margin = new Thickness(viewportSelection.Left, viewportSelection.Top, 0, 0);
            SelectionBox.Width = Math.Max(0, viewportSelection.Width);
            SelectionBox.Height = Math.Max(0, viewportSelection.Height);

            var ctrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            foreach (var rule in _viewModel.Rules)
            {
                if (RulesListView.ItemContainerGenerator.ContainerFromItem(rule) is not ListViewItem container)
                    continue;

                var itemBounds = container.TransformToAncestor(RulesListView)
                    .TransformBounds(new Rect(new Point(), container.RenderSize));
                itemBounds.Offset(horizontalOffset, verticalOffset);
                var shouldSelect = contentSelection.IntersectsWith(itemBounds) ||
                    (ctrlPressed && _initialSelection.Contains(rule));
                if (shouldSelect && !RulesListView.SelectedItems.Contains(rule))
                    RulesListView.SelectedItems.Add(rule);
                else if (!shouldSelect && RulesListView.SelectedItems.Contains(rule))
                    RulesListView.SelectedItems.Remove(rule);
            }
        }

        private void RulesListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isBoxSelecting)
            {
                _hasPendingBoxSelection = false;
                return;
            }

            UpdateBoxSelection(e.GetPosition(RulesListView));
            _isBoxSelecting = false;
            _hasPendingBoxSelection = false;
            SelectionBox.Visibility = Visibility.Collapsed;
            RulesListView.ReleaseMouseCapture();
        }

        private void RulesListView_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isBoxSelecting)
                CancelBoxSelection();
        }

        private void CancelBoxSelection()
        {
            _hasPendingBoxSelection = false;
            if (!_isBoxSelecting)
                return;

            _isBoxSelecting = false;
            RulesListView.ReleaseMouseCapture();
            RulesListView.SelectedItems.Clear();
            foreach (var rule in _initialSelection)
                RulesListView.SelectedItems.Add(rule);
            SelectionBox.Visibility = Visibility.Collapsed;
        }

        private Point GetContentPoint(Point viewportPoint)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(RulesListView);
            var point = ClampSelectionPoint(
                viewportPoint,
                new Size(RulesListView.ActualWidth, RulesListView.ActualHeight));
            if (GetItemAreaBounds() is { } itemArea)
                point.Y = Math.Clamp(point.Y, itemArea.Top, itemArea.Bottom);
            return new Point(
                point.X + (scrollViewer?.HorizontalOffset ?? 0),
                point.Y + (scrollViewer?.VerticalOffset ?? 0));
        }

        private Rect? GetItemAreaBounds()
        {
            var itemBounds = _viewModel.Rules
                .Select(rule => RulesListView.ItemContainerGenerator.ContainerFromItem(rule))
                .OfType<ListViewItem>()
                .Select(container => container.TransformToAncestor(RulesListView)
                    .TransformBounds(new Rect(new Point(), container.RenderSize)))
                .ToArray();
            if (itemBounds.Length == 0)
                return null;

            var top = itemBounds.Min(bounds => bounds.Top);
            var bottom = itemBounds.Max(bounds => bounds.Bottom);
            return new Rect(0, top, RulesListView.ActualWidth, bottom - top);
        }

        private void ScrollForBoxSelection(Point point)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(RulesListView);
            if (scrollViewer is null || GetItemAreaBounds() is not { } itemArea)
                return;

            if (point.Y < itemArea.Top)
            {
                var distance = itemArea.Top - point.Y;
                scrollViewer.ScrollToVerticalOffset(
                    scrollViewer.VerticalOffset - GetBoxScrollStep(distance));
            }
            else if (point.Y > itemArea.Bottom)
            {
                var distance = point.Y - itemArea.Bottom;
                scrollViewer.ScrollToVerticalOffset(
                    scrollViewer.VerticalOffset + GetBoxScrollStep(distance));
            }
        }

        internal static Point ClampSelectionPoint(Point point, Size bounds) => new(
            Math.Clamp(point.X, 0, Math.Max(0, bounds.Width)),
            Math.Clamp(point.Y, 0, Math.Max(0, bounds.Height)));

        internal static double GetBoxScrollStep(double distance) =>
            distance <= 0 ? 0 : Math.Min(14, Math.Max(1, distance * 0.12));

        private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
        {
            while (source is not null)
            {
                if (source is T match)
                    return match;
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        private static T? FindVisualChild<T>(DependencyObject? source) where T : DependencyObject
        {
            if (source is null)
                return null;

            for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(source); index++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(source, index);
                if (child is T match)
                    return match;

                if (FindVisualChild<T>(child) is { } descendant)
                    return descendant;
            }

            return null;
        }

        private void EditRule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is EventRule rule)
            {
                var dialog = new EventRuleEditorDialog(rule);
                dialog.ShowDialog();
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            IEnumerable<EventRule>? selected = RulesListView.SelectedItems.Count > 0
                ? RulesListView.SelectedItems.Cast<EventRule>()
                : null;
            _viewModel.ExportRules(selected);
        }
    }
}
