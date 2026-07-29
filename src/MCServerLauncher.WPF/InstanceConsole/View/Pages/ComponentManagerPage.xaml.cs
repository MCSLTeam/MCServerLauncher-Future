using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MCServerLauncher.WPF.InstanceConsole.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MCServerLauncher.WPF.InstanceConsole.View.Pages
{
    public partial class ComponentManagerPage
    {
        private readonly ComponentManagerViewModel _viewModel;
        private ListView? _selectionListView;
        private Border? _selectionBox;
        private Point _selectionStart;
        private Point _pendingSelectionStart;
        private bool _hasPendingBoxSelection;
        private bool _isBoxSelecting;
        private HashSet<ComponentItemModel> _initialSelection = [];
        private System.Windows.Window? _hostWindow;

        public ComponentManagerPage()
        {
            InitializeComponent();
            _viewModel = App.Services.GetRequiredService<ComponentManagerViewModel>();
            DataContext = _viewModel;
            Loaded += async (_, _) => await _viewModel.InitializeAsync();
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

        private async void Page_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                await _viewModel.HandleDroppedFilesAsync(files);
            }
        }

        private void Page_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                bool hasJar = files?.Any(f => f.EndsWith(".jar", System.StringComparison.OrdinalIgnoreCase)) == true;
                e.Effects = hasJar ? DragDropEffects.Copy : DragDropEffects.None;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void ComponentListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListView listView || GetSelectionBox(listView) is not { } selectionBox)
                return;

            _selectionListView = listView;
            _selectionBox = selectionBox;
            _pendingSelectionStart = e.GetPosition(listView);
            _selectionStart = GetContentPoint(listView, _pendingSelectionStart);
            _initialSelection = listView.SelectedItems.Cast<ComponentItemModel>().ToHashSet();
            _hasPendingBoxSelection = true;

            if (FindVisualParent<ListViewItem>(e.OriginalSource as DependencyObject) is not null)
                return;

            BeginBoxSelection();
            UpdateBoxSelection(_pendingSelectionStart);
            e.Handled = true;
        }

        private void ComponentListView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not ListView listView ||
                !ReferenceEquals(listView, _selectionListView) ||
                e.LeftButton != MouseButtonState.Pressed)
                return;

            var current = e.GetPosition(listView);
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

            ScrollForBoxSelection(listView, current);
            UpdateBoxSelection(current);
            e.Handled = true;
        }

        private void BeginBoxSelection()
        {
            if (_selectionListView is null)
                return;

            _selectionStart = GetContentPoint(_selectionListView, _pendingSelectionStart);
            _hasPendingBoxSelection = false;
            _isBoxSelecting = true;
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                _selectionListView.SelectedItems.Clear();
            _selectionListView.CaptureMouse();
        }

        private void ComponentListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListView listView ||
                !ReferenceEquals(listView, _selectionListView))
                return;

            if (!_isBoxSelecting)
            {
                _hasPendingBoxSelection = false;
                return;
            }

            UpdateBoxSelection(e.GetPosition(listView));
            _isBoxSelecting = false;
            _hasPendingBoxSelection = false;
            if (_selectionBox is not null)
                _selectionBox.Visibility = Visibility.Collapsed;
            listView.ReleaseMouseCapture();
        }

        private void ComponentListView_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isBoxSelecting)
                CancelBoxSelection();
        }

        private void CancelBoxSelection()
        {
            _hasPendingBoxSelection = false;
            if (_selectionListView is null || !_isBoxSelecting)
                return;

            _isBoxSelecting = false;
            _selectionListView.ReleaseMouseCapture();
            _selectionListView.SelectedItems.Clear();
            foreach (var item in _initialSelection)
                _selectionListView.SelectedItems.Add(item);
            if (_selectionBox is not null)
                _selectionBox.Visibility = Visibility.Collapsed;
        }

        private void UpdateBoxSelection(Point viewportPoint)
        {
            if (_selectionListView is not { } listView || _selectionBox is not { } selectionBox)
                return;

            var scrollViewer = FindVisualChild<ScrollViewer>(listView);
            var horizontalOffset = scrollViewer?.HorizontalOffset ?? 0;
            var verticalOffset = scrollViewer?.VerticalOffset ?? 0;
            var current = GetContentPoint(listView, viewportPoint);
            var contentSelection = new Rect(_selectionStart, current);
            var viewportSelection = new Rect(
                new Point(contentSelection.Left - horizontalOffset, contentSelection.Top - verticalOffset),
                new Point(contentSelection.Right - horizontalOffset, contentSelection.Bottom - verticalOffset));
            if (GetItemAreaBounds(listView) is { } itemArea)
                viewportSelection.Intersect(itemArea);
            viewportSelection.Intersect(new Rect(0, 0, listView.ActualWidth, listView.ActualHeight));

            selectionBox.Visibility = Visibility.Visible;
            selectionBox.HorizontalAlignment = HorizontalAlignment.Left;
            selectionBox.VerticalAlignment = VerticalAlignment.Top;
            selectionBox.Margin = new Thickness(viewportSelection.Left, viewportSelection.Top, 0, 0);
            selectionBox.Width = Math.Max(0, viewportSelection.Width);
            selectionBox.Height = Math.Max(0, viewportSelection.Height);

            var ctrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            foreach (var item in listView.Items.OfType<ComponentItemModel>())
            {
                if (listView.ItemContainerGenerator.ContainerFromItem(item) is not ListViewItem container)
                    continue;

                var itemBounds = container.TransformToAncestor(listView)
                    .TransformBounds(new Rect(new Point(), container.RenderSize));
                itemBounds.Offset(horizontalOffset, verticalOffset);
                var shouldSelect = contentSelection.IntersectsWith(itemBounds) ||
                    (ctrlPressed && _initialSelection.Contains(item));
                if (shouldSelect && !listView.SelectedItems.Contains(item))
                    listView.SelectedItems.Add(item);
                else if (!shouldSelect && listView.SelectedItems.Contains(item))
                    listView.SelectedItems.Remove(item);
            }
        }

        private Point GetContentPoint(ListView listView, Point viewportPoint)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(listView);
            var point = ClampSelectionPoint(
                viewportPoint,
                new Size(listView.ActualWidth, listView.ActualHeight));
            if (GetItemAreaBounds(listView) is { } itemArea)
                point.Y = Math.Clamp(point.Y, itemArea.Top, itemArea.Bottom);
            return new Point(
                point.X + (scrollViewer?.HorizontalOffset ?? 0),
                point.Y + (scrollViewer?.VerticalOffset ?? 0));
        }

        private static Rect? GetItemAreaBounds(ListView listView)
        {
            var itemBounds = listView.Items
                .OfType<ComponentItemModel>()
                .Select(item => listView.ItemContainerGenerator.ContainerFromItem(item))
                .OfType<ListViewItem>()
                .Select(container => container.TransformToAncestor(listView)
                    .TransformBounds(new Rect(new Point(), container.RenderSize)))
                .ToArray();
            if (itemBounds.Length == 0)
                return null;

            var top = itemBounds.Min(bounds => bounds.Top);
            var bottom = itemBounds.Max(bounds => bounds.Bottom);
            return new Rect(0, top, listView.ActualWidth, bottom - top);
        }

        private void ScrollForBoxSelection(ListView listView, Point point)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(listView);
            if (scrollViewer is null || GetItemAreaBounds(listView) is not { } itemArea)
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

        private Border? GetSelectionBox(ListView listView) =>
            ReferenceEquals(listView, ModsListView) ? ModsSelectionBox :
            ReferenceEquals(listView, PluginsListView) ? PluginsSelectionBox : null;

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
    }
}
