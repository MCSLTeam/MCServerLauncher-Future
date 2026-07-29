using MCServerLauncher.WPF.ViewModels;
using MCServerLauncher.WPF.ViewModels.Models;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.Generic;
using System.Linq;

namespace MCServerLauncher.WPF.View.Pages
{
    public partial class DaemonManagerPage
    {
        private readonly DaemonManagerViewModel _viewModel;
        private System.Windows.Threading.DispatcherTimer? _refreshTimer;
        private Point _selectionStart;
        private Point _pendingSelectionStart;
        private bool _hasPendingBoxSelection;
        private bool _isBoxSelecting;
        private HashSet<DaemonCardModel> _initialSelection = [];

        public DaemonManagerPage()
        {
            InitializeComponent();
            _viewModel = App.ViewModelLocator.DaemonManager;
            DataContext = _viewModel;

            IsVisibleChanged += async (s, e) =>
            {
                if (IsVisible)
                {
                    await _viewModel.RefreshCommand.ExecuteAsync(null);
                    StartAutoRefresh();
                }
                else
                {
                    StopAutoRefresh();
                }
            };

        }

        private void StartAutoRefresh()
        {
            if (!IsVisible)
            {
                StopAutoRefresh();
                return;
            }

            _refreshTimer ??= new System.Windows.Threading.DispatcherTimer();
            _refreshTimer.Tick -= RefreshTimerTick;
            _refreshTimer.Tick += RefreshTimerTick;
            _refreshTimer.Interval = TimeSpan.FromSeconds(5);
            _refreshTimer.Start();
        }

        private async void RefreshTimerTick(object? sender, EventArgs e)
        {
            await _viewModel.AutoRefreshCommand.ExecuteAsync(null);
        }

        private void StopAutoRefresh()
        {
            _refreshTimer?.Stop();
        }

        private async void EditDaemonMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: DaemonCardModel daemon })
            {
                await _viewModel.EditDaemonCommand.ExecuteAsync(daemon);
            }
        }

        private async void DeleteDaemonMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: DaemonCardModel daemon })
            {
                await _viewModel.DeleteDaemonCommand.ExecuteAsync(daemon);
            }
        }

        private async void ShowDaemonErrorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: DaemonCardModel daemon }) return;

            var dialog = new ContentDialog
            {
                Title = Modules.Lang.Tr["ConnectDaemonFailedTip"],
                Content = string.IsNullOrWhiteSpace(daemon.LastErrorMessage)
                    ? Modules.Lang.Tr["ConnectDaemonFailedSubTip"]
                    : daemon.LastErrorMessage,
                CloseButtonText = Modules.Lang.Tr["OK"],
                DefaultButton = ContentDialogButton.Close
            };

            try { await dialog.ShowAsync(); }
            catch { }
        }

        public Task OpenAddConnectionAsync()
        {
            return _viewModel.AddConnectionCommand.ExecuteAsync(null);
        }

        private void CardListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pendingSelectionStart = e.GetPosition(DaemonCardItemsControl);
            _selectionStart = GetContentPoint(_pendingSelectionStart);
            _initialSelection = DaemonCardItemsControl.SelectedItems.Cast<DaemonCardModel>().ToHashSet();
            _hasPendingBoxSelection = true;

            if (FindVisualParent<iNKORE.UI.WPF.Modern.Controls.GridViewItem>(e.OriginalSource as DependencyObject) is not null)
                return;

            BeginBoxSelection();
            UpdateBoxSelection(_pendingSelectionStart);
            e.Handled = true;
        }

        private void CardListView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            var current = e.GetPosition(DaemonCardItemsControl);
            if (!_isBoxSelecting && _hasPendingBoxSelection)
            {
                if (Math.Abs(current.X - _pendingSelectionStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(current.Y - _pendingSelectionStart.Y) < SystemParameters.MinimumVerticalDragDistance)
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
                DaemonCardItemsControl.SelectedItems.Clear();
            DaemonCardItemsControl.CaptureMouse();
        }

        private void CardListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isBoxSelecting)
            {
                _hasPendingBoxSelection = false;
                return;
            }

            UpdateBoxSelection(e.GetPosition(DaemonCardItemsControl));
            _isBoxSelecting = false;
            _hasPendingBoxSelection = false;
            SelectionBox.Visibility = Visibility.Collapsed;
            DaemonCardItemsControl.ReleaseMouseCapture();
        }

        private void UpdateBoxSelection(Point viewportPoint)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(DaemonCardItemsControl);
            var horizontalOffset = scrollViewer?.HorizontalOffset ?? 0;
            var verticalOffset = scrollViewer?.VerticalOffset ?? 0;
            var current = GetContentPoint(viewportPoint);
            var contentSelection = new Rect(_selectionStart, current);
            var viewportSelection = new Rect(
                new Point(contentSelection.Left - horizontalOffset, contentSelection.Top - verticalOffset),
                new Point(contentSelection.Right - horizontalOffset, contentSelection.Bottom - verticalOffset));
            viewportSelection.Intersect(new Rect(0, 0, DaemonCardItemsControl.ActualWidth, DaemonCardItemsControl.ActualHeight));

            SelectionBox.Visibility = Visibility.Visible;
            SelectionBox.HorizontalAlignment = HorizontalAlignment.Left;
            SelectionBox.VerticalAlignment = VerticalAlignment.Top;
            SelectionBox.Margin = new Thickness(viewportSelection.Left, viewportSelection.Top, 0, 0);
            SelectionBox.Width = Math.Max(0, viewportSelection.Width);
            SelectionBox.Height = Math.Max(0, viewportSelection.Height);

            var ctrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            foreach (var item in _viewModel.FilteredDaemons)
            {
                if (DaemonCardItemsControl.ItemContainerGenerator.ContainerFromItem(item) is not iNKORE.UI.WPF.Modern.Controls.GridViewItem container)
                    continue;

                var itemBounds = container.TransformToAncestor(DaemonCardItemsControl)
                    .TransformBounds(new Rect(new Point(), container.RenderSize));
                itemBounds.Offset(horizontalOffset, verticalOffset);
                var shouldSelect = contentSelection.IntersectsWith(itemBounds) ||
                    (ctrlPressed && _initialSelection.Contains(item));
                if (shouldSelect && !DaemonCardItemsControl.SelectedItems.Contains(item))
                    DaemonCardItemsControl.SelectedItems.Add(item);
                else if (!shouldSelect && DaemonCardItemsControl.SelectedItems.Contains(item))
                    DaemonCardItemsControl.SelectedItems.Remove(item);
            }
        }

        private Point GetContentPoint(Point viewportPoint)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(DaemonCardItemsControl);
            var point = ClampSelectionPoint(viewportPoint, new Size(DaemonCardItemsControl.ActualWidth, DaemonCardItemsControl.ActualHeight));
            return new Point(point.X + (scrollViewer?.HorizontalOffset ?? 0), point.Y + (scrollViewer?.VerticalOffset ?? 0));
        }

        private Rect? GetItemAreaBounds()
        {
            var itemBounds = _viewModel.FilteredDaemons
                .Select(item => DaemonCardItemsControl.ItemContainerGenerator.ContainerFromItem(item))
                .OfType<iNKORE.UI.WPF.Modern.Controls.GridViewItem>()
                .Select(container => container.TransformToAncestor(DaemonCardItemsControl).TransformBounds(new Rect(new Point(), container.RenderSize)))
                .ToArray();
            if (itemBounds.Length == 0)
                return null;

            var top = itemBounds.Min(bounds => bounds.Top);
            var bottom = itemBounds.Max(bounds => bounds.Bottom);
            return new Rect(0, top, DaemonCardItemsControl.ActualWidth, bottom - top);
        }

        private void ScrollForBoxSelection(Point point)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(DaemonCardItemsControl);
            if (scrollViewer is null)
                return;

            if (point.Y < 0)
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - GetBoxScrollStep(-point.Y));
            else if (point.Y > DaemonCardItemsControl.ActualHeight)
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + GetBoxScrollStep(point.Y - DaemonCardItemsControl.ActualHeight));
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
    }
}
