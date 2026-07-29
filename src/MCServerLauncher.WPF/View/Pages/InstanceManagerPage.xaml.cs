using iNKORE.UI.WPF.Modern.Common.IconKeys;
using CommunityToolkit.Mvvm.Input;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.ViewModels;
using MCServerLauncher.WPF.ViewModels.Models;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Collections.Generic;
using System.Linq;

namespace MCServerLauncher.WPF.View.Pages
{
    public partial class InstanceManagerPage
    {
        private readonly InstanceManagerViewModel _viewModel;
        private System.Windows.Threading.DispatcherTimer? _refreshTimer;
        private Point _selectionStart;
        private Point _pendingSelectionStart;
        private bool _hasPendingBoxSelection;
        private bool _isBoxSelecting;
        private HashSet<InstanceCardModel> _initialSelection = [];

        public InstanceManagerPage()
        {
            _viewModel = App.ViewModelLocator.InstanceManager;
            InitializeComponent();
            DataContext = _viewModel;
            StopTipLayer.ButtonCommand = new AsyncRelayCommand(OpenDaemonManagerConnectionAsync);

            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            _viewModel.LoadDaemonFilterItems();

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

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(InstanceManagerViewModel.IsLoading):
                    if (_viewModel.IsLoading) ShowLoadingLayer();
                    else HideLoadingLayer();
                    break;
                case nameof(InstanceManagerViewModel.ErrorState):
                    UpdateErrorState();
                    break;
            }
        }

        private void UpdateErrorState()
        {
            LoadingLayer.BeginAnimation(OpacityProperty, null);
            LoadingLayer.Visibility = Visibility.Collapsed;
            StopTipLayer.Visibility = Visibility.Collapsed;
            StopTipLayer.ButtonCommand = null;
            InstanceCardGrid.Visibility = Visibility.Visible;
            FilterBar.Visibility = Visibility.Visible;

            switch (_viewModel.ErrorState)
            {
                case "no_daemon":
                    InstanceCardGrid.Visibility = Visibility.Collapsed;
                    FilterBar.Visibility = Visibility.Collapsed;
                    StopTipLayer.Symbol = "❌";
                    StopTipLayer.StopTip = Lang.Tr["FuncDisabled"];
                    StopTipLayer.StopDescription = Lang.Tr["FuncDisabledReason_NoDaemon"];
                    StopTipLayer.ButtonIcon = SegoeFluentIcons.ConnectApp;
                    StopTipLayer.ButtonText = Lang.Tr["ConnectDaemon"];
                    StopTipLayer.ButtonCommand = new AsyncRelayCommand(OpenDaemonManagerConnectionAsync);
                    StopTipLayer.Visibility = Visibility.Visible;
                    break;
                case "no_instance":
                    InstanceCardGrid.Visibility = Visibility.Collapsed;
                    StopTipLayer.Symbol = "🤔";
                    StopTipLayer.StopTip = Lang.Tr["NothingHere"];
                    StopTipLayer.StopDescription = Lang.Tr["TryAddSomething"];
                    StopTipLayer.ButtonIcon = SegoeFluentIcons.AddTo;
                    StopTipLayer.ButtonText = Lang.Tr["Main_CreateInstanceNavMenu"];
                    StopTipLayer.Visibility = Visibility.Visible;
                    break;
                case "load_error":
                    InstanceCardGrid.Visibility = Visibility.Collapsed;
                    StopTipLayer.Symbol = "❌";
                    StopTipLayer.StopTip = Lang.Tr["ConnectDaemonFailedTip"];
                    StopTipLayer.StopDescription = Lang.Tr["ConnectDaemonFailedSubTip"];
                    StopTipLayer.ButtonIcon = SegoeFluentIcons.Sync;
                    StopTipLayer.ButtonText = Lang.Tr["Refresh"];
                    StopTipLayer.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void StartAutoRefresh()
        {
            if (!IsVisible)
            {
                StopAutoRefresh();
                return;
            }

            if (_refreshTimer == null)
            {
                _refreshTimer = new System.Windows.Threading.DispatcherTimer();
                _refreshTimer.Tick += async (s, e) => await _viewModel.AutoRefreshCommand.ExecuteAsync(null);
            }
            _refreshTimer.Interval = TimeSpan.FromSeconds(5);
            _refreshTimer.Start();
        }

        private void StopAutoRefresh()
        {
            _refreshTimer?.Stop();
        }

        private void DaemonFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox) return;
            _ = _viewModel.RefreshCommand.ExecuteAsync(null);
        }

        private void RunningStatusFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel == null) return;
            if (RunningStatusFilter.SelectedItem is ComboBoxItem selectedItem)
            {
                _viewModel.SelectedStatusFilter = selectedItem.Tag?.ToString() ?? "All";
                _viewModel.ApplyFilters();
            }
        }

        private void ShowLoadingLayer()
        {
            StopTipLayer.Visibility = Visibility.Collapsed;
            InstanceCardGrid.Visibility = Visibility.Visible;
            LoadingLayer.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromSeconds(0.4))) { FillBehavior = FillBehavior.HoldEnd };
            LoadingLayer.BeginAnimation(OpacityProperty, fadeIn);
        }

        private void HideLoadingLayer()
        {
            var fadeOut = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromSeconds(0.4))) { FillBehavior = FillBehavior.HoldEnd };
            fadeOut.Completed += (s, e) => LoadingLayer.Visibility = Visibility.Collapsed;
            LoadingLayer.BeginAnimation(OpacityProperty, fadeOut);
        }

        private async System.Threading.Tasks.Task OpenDaemonManagerConnectionAsync()
        {
            await MCServerLauncher.WPF.Modules.VisualTreeHelper.NavigateToDaemonManagerAndOpenConnectionAsync();
        }

        private void CardListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pendingSelectionStart = e.GetPosition(InstanceCardGrid);
            _selectionStart = GetContentPoint(_pendingSelectionStart);
            _initialSelection = InstanceCardGrid.SelectedItems.Cast<InstanceCardModel>().ToHashSet();
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

            var current = e.GetPosition(InstanceCardGrid);
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
                InstanceCardGrid.SelectedItems.Clear();
            InstanceCardGrid.CaptureMouse();
        }

        private void CardListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isBoxSelecting)
            {
                _hasPendingBoxSelection = false;
                return;
            }

            UpdateBoxSelection(e.GetPosition(InstanceCardGrid));
            _isBoxSelecting = false;
            _hasPendingBoxSelection = false;
            SelectionBox.Visibility = Visibility.Collapsed;
            InstanceCardGrid.ReleaseMouseCapture();
        }

        private void UpdateBoxSelection(Point viewportPoint)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(InstanceCardGrid);
            var horizontalOffset = scrollViewer?.HorizontalOffset ?? 0;
            var verticalOffset = scrollViewer?.VerticalOffset ?? 0;
            var current = GetContentPoint(viewportPoint);
            var contentSelection = new Rect(_selectionStart, current);
            var viewportSelection = new Rect(
                new Point(contentSelection.Left - horizontalOffset, contentSelection.Top - verticalOffset),
                new Point(contentSelection.Right - horizontalOffset, contentSelection.Bottom - verticalOffset));
            if (GetItemAreaBounds() is { } itemArea)
                viewportSelection.Intersect(itemArea);
            viewportSelection.Intersect(new Rect(0, 0, InstanceCardGrid.ActualWidth, InstanceCardGrid.ActualHeight));

            SelectionBox.Visibility = Visibility.Visible;
            SelectionBox.HorizontalAlignment = HorizontalAlignment.Left;
            SelectionBox.VerticalAlignment = VerticalAlignment.Top;
            SelectionBox.Margin = new Thickness(viewportSelection.Left, viewportSelection.Top, 0, 0);
            SelectionBox.Width = Math.Max(0, viewportSelection.Width);
            SelectionBox.Height = Math.Max(0, viewportSelection.Height);

            var ctrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            foreach (var item in _viewModel.FilteredInstances)
            {
                if (InstanceCardGrid.ItemContainerGenerator.ContainerFromItem(item) is not iNKORE.UI.WPF.Modern.Controls.GridViewItem container)
                    continue;

                var itemBounds = container.TransformToAncestor(InstanceCardGrid)
                    .TransformBounds(new Rect(new Point(), container.RenderSize));
                itemBounds.Offset(horizontalOffset, verticalOffset);
                var shouldSelect = contentSelection.IntersectsWith(itemBounds) ||
                    (ctrlPressed && _initialSelection.Contains(item));
                if (shouldSelect && !InstanceCardGrid.SelectedItems.Contains(item))
                    InstanceCardGrid.SelectedItems.Add(item);
                else if (!shouldSelect && InstanceCardGrid.SelectedItems.Contains(item))
                    InstanceCardGrid.SelectedItems.Remove(item);
            }
        }

        private Point GetContentPoint(Point viewportPoint)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(InstanceCardGrid);
            var point = ClampSelectionPoint(viewportPoint, new Size(InstanceCardGrid.ActualWidth, InstanceCardGrid.ActualHeight));
            if (GetItemAreaBounds() is { } itemArea)
                point.Y = Math.Clamp(point.Y, itemArea.Top, itemArea.Bottom);
            return new Point(point.X + (scrollViewer?.HorizontalOffset ?? 0), point.Y + (scrollViewer?.VerticalOffset ?? 0));
        }

        private Rect? GetItemAreaBounds()
        {
            var itemBounds = _viewModel.FilteredInstances
                .Select(item => InstanceCardGrid.ItemContainerGenerator.ContainerFromItem(item))
                .OfType<ListBoxItem>()
                .Select(container => container.TransformToAncestor(InstanceCardGrid).TransformBounds(new Rect(new Point(), container.RenderSize)))
                .ToArray();
            if (itemBounds.Length == 0)
                return null;

            var top = itemBounds.Min(bounds => bounds.Top);
            var bottom = itemBounds.Max(bounds => bounds.Bottom);
            return new Rect(0, top, InstanceCardGrid.ActualWidth, bottom - top);
        }

        private void ScrollForBoxSelection(Point point)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(InstanceCardGrid);
            if (scrollViewer is null || GetItemAreaBounds() is not { } itemArea)
                return;

            if (point.Y < itemArea.Top)
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - GetBoxScrollStep(itemArea.Top - point.Y));
            else if (point.Y > itemArea.Bottom)
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + GetBoxScrollStep(point.Y - itemArea.Bottom));
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
