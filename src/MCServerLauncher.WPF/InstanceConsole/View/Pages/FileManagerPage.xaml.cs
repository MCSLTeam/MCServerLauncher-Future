using System;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;
using Microsoft.Win32;
using Serilog;
using TypedDaemonClient = MCServerLauncher.DaemonClient.DaemonClient;

namespace MCServerLauncher.WPF.InstanceConsole.View.Pages
{
    public partial class FileManagerPage
    {
        private FileManagerViewModel _viewModel;
        private Point _selectionStart;
        private Point _pendingSelectionStart;
        private bool _hasPendingBoxSelection;
        private bool _isBoxSelecting;
        private HashSet<FileItem> _initialSelection = [];
        private System.Windows.Window? _hostWindow;

        public FileManagerPage()
        {
            InitializeComponent();
            _viewModel = new FileManagerViewModel();
            DataContext = _viewModel;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            Loaded += FileManagerPage_Loaded;
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

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileManagerViewModel.HasError))
            {
                if (_viewModel.HasError)
                {
                    StopTipLayer.Symbol = "❌";
                    StopTipLayer.StopTip = _viewModel.ErrorTitle;
                    StopTipLayer.StopDescription = _viewModel.ErrorMessage;
                    StopTipLayer.ButtonIcon = iNKORE.UI.WPF.Modern.Common.IconKeys.SegoeFluentIcons.Refresh;
                    StopTipLayer.ButtonText = Lang.Tr["Refresh"];
                    StopTipLayer.Visibility = Visibility.Visible;
                    MainContentGrid.Visibility = Visibility.Collapsed;
                }
                else
                {
                    StopTipLayer.Visibility = Visibility.Collapsed;
                    MainContentGrid.Visibility = Visibility.Visible;
                }
            }
        }

        private async void FileManagerPage_Loaded(object sender, RoutedEventArgs e) =>
            await _viewModel.InitializeAsync();

        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.SelectedItem != null)
            {
                if (_viewModel.OpenCommand.CanExecute(null))
                {
                    _viewModel.OpenCommand.Execute(null);
                }
            }
        }

        private void FileListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pendingSelectionStart = e.GetPosition(FileListView);
            _selectionStart = GetContentPoint(_pendingSelectionStart);
            _initialSelection = FileListView.SelectedItems.Cast<FileItem>().ToHashSet();
            _hasPendingBoxSelection = true;

            if (FindVisualParent<ListViewItem>(e.OriginalSource as DependencyObject) is not null)
                return;

            BeginBoxSelection();
            UpdateBoxSelection(_pendingSelectionStart);
            e.Handled = true;
        }

        private void FileListView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            var current = e.GetPosition(FileListView);
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
                FileListView.SelectedItems.Clear();
            FileListView.CaptureMouse();
        }

        private void UpdateBoxSelection(Point viewportPoint)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(FileListView);
            var horizontalOffset = scrollViewer?.HorizontalOffset ?? 0;
            var verticalOffset = scrollViewer?.VerticalOffset ?? 0;
            var current = GetContentPoint(viewportPoint);
            var contentSelection = new Rect(_selectionStart, current);
            var itemArea = GetItemAreaBounds();
            var viewportSelection = new Rect(
                new Point(contentSelection.Left - horizontalOffset, contentSelection.Top - verticalOffset),
                new Point(contentSelection.Right - horizontalOffset, contentSelection.Bottom - verticalOffset));
            var listBounds = new Rect(0, 0, FileListView.ActualWidth, FileListView.ActualHeight);
            if (itemArea is { } visibleItemArea)
                viewportSelection.Intersect(visibleItemArea);
            viewportSelection.Intersect(listBounds);

            SelectionBox.Visibility = Visibility.Visible;
            SelectionBox.HorizontalAlignment = HorizontalAlignment.Left;
            SelectionBox.VerticalAlignment = VerticalAlignment.Top;
            SelectionBox.Margin = new Thickness(viewportSelection.Left, viewportSelection.Top, 0, 0);
            SelectionBox.Width = Math.Max(0, viewportSelection.Width);
            SelectionBox.Height = Math.Max(0, viewportSelection.Height);

            var ctrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            foreach (var item in _viewModel.Items)
            {
                if (FileListView.ItemContainerGenerator.ContainerFromItem(item) is not ListViewItem container)
                    continue;

                var itemBounds = container.TransformToAncestor(FileListView)
                    .TransformBounds(new Rect(new Point(), container.RenderSize));
                itemBounds.Offset(horizontalOffset, verticalOffset);
                var shouldSelect = IntersectsSelection(contentSelection, itemBounds) ||
                    (ctrlPressed && _initialSelection.Contains(item));
                if (shouldSelect && !FileListView.SelectedItems.Contains(item))
                    FileListView.SelectedItems.Add(item);
                else if (!shouldSelect && FileListView.SelectedItems.Contains(item))
                    FileListView.SelectedItems.Remove(item);
            }
        }

        private void FileListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isBoxSelecting)
            {
                _hasPendingBoxSelection = false;
                return;
            }

            UpdateBoxSelection(e.GetPosition(FileListView));
            _isBoxSelecting = false;
            SelectionBox.Visibility = Visibility.Collapsed;
            FileListView.ReleaseMouseCapture();
            _viewModel.SelectedItem = FileListView.SelectedItems.Cast<FileItem>().FirstOrDefault();
        }

        private void FileListView_LostMouseCapture(object sender, MouseEventArgs e)
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
            FileListView.ReleaseMouseCapture();
            FileListView.SelectedItems.Clear();
            foreach (var item in _initialSelection)
                FileListView.SelectedItems.Add(item);
            SelectionBox.Visibility = Visibility.Collapsed;
            _viewModel.SelectedItem = FileListView.SelectedItems.Cast<FileItem>().FirstOrDefault();
        }

        internal static bool IntersectsSelection(Rect selection, Rect itemBounds) =>
            selection.IntersectsWith(itemBounds);

        internal static Point ClampSelectionPoint(Point point, Size bounds) => new(
            Math.Clamp(point.X, 0, Math.Max(0, bounds.Width)),
            Math.Clamp(point.Y, 0, Math.Max(0, bounds.Height)));

        internal static double GetBoxScrollStep(double distance) =>
            distance <= 0 ? 0 : Math.Min(14, Math.Max(1, distance * 0.12));

        private Point GetContentPoint(Point viewportPoint)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(FileListView);
            var point = ClampSelectionPoint(
                viewportPoint,
                new Size(FileListView.ActualWidth, FileListView.ActualHeight));
            var itemArea = GetItemAreaBounds();
            if (itemArea is { } bounds)
                point.Y = Math.Clamp(point.Y, bounds.Top, bounds.Bottom);
            return new Point(
                point.X + (scrollViewer?.HorizontalOffset ?? 0),
                point.Y + (scrollViewer?.VerticalOffset ?? 0));
        }

        private Rect? GetItemAreaBounds()
        {
            var itemBounds = _viewModel.Items
                .Select(item => FileListView.ItemContainerGenerator.ContainerFromItem(item))
                .OfType<ListViewItem>()
                .Select(container => container.TransformToAncestor(FileListView)
                    .TransformBounds(new Rect(new Point(), container.RenderSize)))
                .ToArray();
            if (itemBounds.Length == 0)
                return null;

            var top = itemBounds.Min(bounds => bounds.Top);
            var bottom = itemBounds.Max(bounds => bounds.Bottom);
            return new Rect(0, top, FileListView.ActualWidth, bottom - top);
        }

        private void ScrollForBoxSelection(Point point)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(FileListView);
            if (scrollViewer is null)
                return;

            var itemArea = GetItemAreaBounds();
            if (itemArea is not { } bounds)
                return;

            if (point.Y < bounds.Top)
            {
                var distance = bounds.Top - point.Y;
                scrollViewer.ScrollToVerticalOffset(
                    scrollViewer.VerticalOffset - GetBoxScrollStep(distance));
            }
            else if (point.Y > bounds.Bottom)
            {
                var distance = point.Y - bounds.Bottom;
                scrollViewer.ScrollToVerticalOffset(
                    scrollViewer.VerticalOffset + GetBoxScrollStep(distance));
            }
        }

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

    public class FileItem : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long SizeBytes { get; set; }
        public long ModifiedTime { get; set; }

        public string Icon => IsDirectory ? "\uE8B7" : "\uE8A5"; // Folder icon vs File icon
        public string Type => IsDirectory ? "文件夹" : "文件";
        public string Size => IsDirectory ? "" : FormatSize(SizeBytes);
        public string ModifiedDate => DateTimeOffset.FromUnixTimeSeconds(ModifiedTime).ToLocalTime().ToString("yyyy/MM/dd HH:mm");

        private string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double dblSByte = bytes;
            while (dblSByte >= 1024 && i < suffixes.Length - 1)
            {
                dblSByte /= 1024;
                i++;
            }
            return $"{dblSByte:0.##} {suffixes[i]}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class TreeItem : INotifyPropertyChanged
    {
        private readonly FileManagerViewModel _viewModel;
        private bool _isExpanded;
        private bool _isSelected;

        public string Name { get; set; } = string.Empty;
        public string VirtualPath { get; set; } = string.Empty;
        public ObservableCollection<TreeItem> Children { get; } = new();
        public bool IsLoaded { get; private set; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                    if (_isExpanded && !IsLoaded)
                    {
                        _ = LoadChildrenAsync();
                    }
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    if (_isSelected)
                    {
                        _viewModel.OnTreeItemSelected(this);
                    }
                }
            }
        }

        public TreeItem(FileManagerViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        public async Task LoadChildrenAsync()
        {
            if (IsLoaded) return;

            try
            {
                var dirs = await _viewModel.GetDirectoriesAsync(VirtualPath);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Children.Clear();
                    foreach (var dir in dirs)
                    {
                        var child = new TreeItem(_viewModel)
                        {
                            Name = dir.Name,
                            VirtualPath = VirtualPath == "/" ? $"/{dir.Name}" : $"{VirtualPath}/{dir.Name}"
                        };
                        child.Children.Add(new TreeItem(_viewModel) { Name = "Loading..." }); // Dummy
                        Children.Add(child);
                    }
                    IsLoaded = true;
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[FileManager] Failed to load tree children for {0}", VirtualPath);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Children.Clear();
                    Children.Add(new TreeItem(_viewModel) { Name = Lang.Tr["Status_LoadFailed"] });
                    IsLoaded = true;
                });
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    public class FileManagerViewModel : INotifyPropertyChanged
    {
        private TypedDaemonClient? _daemon;
        private string _rootPath = "";
        private string _currentPath = "";
        private FileItem? _selectedItem;
        private ObservableCollection<FileItem> _items = new();
        private ObservableCollection<TreeItem> _treeItems = new();
        private List<string> _history = new();
        private int _historyIndex = -1;
        private bool _isNavigating = false;
        private bool _isSyncingTree = false;
        private bool _hasError;
        private string _errorTitle = "";
        private string _errorMessage = "";

        public bool HasError
        {
            get => _hasError;
            set { _hasError = value; OnPropertyChanged(); }
        }

        public string ErrorTitle
        {
            get => _errorTitle;
            set { _errorTitle = value; OnPropertyChanged(); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        public ObservableCollection<FileItem> Items
        {
            get => _items;
            set { _items = value; OnPropertyChanged(); }
        }

        public ObservableCollection<TreeItem> TreeItems
        {
            get => _treeItems;
            set { _treeItems = value; OnPropertyChanged(); }
        }

        public FileItem? SelectedItem
        {
            get => _selectedItem;
            set { _selectedItem = value; OnPropertyChanged(); }
        }

        public string CurrentPath
        {
            get => _currentPath;
            set { _currentPath = value; OnPropertyChanged(); }
        }

        public ICommand OpenCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand UploadFileCommand { get; }
        public ICommand RenameCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CreateDirectoryCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand UpCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand ForwardCommand { get; }
        public ICommand NavigateCommand { get; }

        public FileManagerViewModel()
        {
            OpenCommand = new RelayCommand(async _ => await OpenItemAsync(), _ => SelectedItem != null);
            DownloadCommand = new RelayCommand(async _ => await DownloadItemAsync(), _ => SelectedItem != null && !SelectedItem.IsDirectory);
            UploadFileCommand = new RelayCommand(async _ => await UploadFileAsync());
            RenameCommand = new RelayCommand(async _ => await RenameItemAsync(), _ => SelectedItem != null);
            DeleteCommand = new RelayCommand(async _ => await DeleteItemAsync(), _ => SelectedItem != null);
            CreateDirectoryCommand = new RelayCommand(async _ => await CreateDirectoryAsync());
            RefreshCommand = new RelayCommand(async _ => await LoadDirectoryAsync(CurrentPath));
            UpCommand = new RelayCommand(async _ => await UpDirectoryAsync(), _ => CanGoUp());
            BackCommand = new RelayCommand(async _ => await GoBackAsync(), _ => CanGoBack());
            ForwardCommand = new RelayCommand(async _ => await GoForwardAsync(), _ => CanGoForward());
            NavigateCommand = new RelayCommand(async _ => await NavigateToPathAsync(CurrentPath));
        }

        private bool CanGoUp()
        {
            return CurrentPath != "/";
        }

        private async Task UpDirectoryAsync()
        {
            if (CurrentPath != "/")
            {
                var parentPath = GetParentVirtualPath(CurrentPath);
                await NavigateToPathAsync(parentPath);
            }
        }

        private bool CanGoBack() => _historyIndex > 0;

        private async Task GoBackAsync()
        {
            if (CanGoBack())
            {
                _historyIndex--;
                _isNavigating = true;
                await LoadDirectoryAsync(_history[_historyIndex]);
                _isNavigating = false;
            }
        }

        private bool CanGoForward() => _historyIndex < _history.Count - 1;

        private async Task GoForwardAsync()
        {
            if (CanGoForward())
            {
                _historyIndex++;
                _isNavigating = true;
                await LoadDirectoryAsync(_history[_historyIndex]);
                _isNavigating = false;
            }
        }

        private async Task NavigateToPathAsync(string path)
        {
            await LoadDirectoryAsync(path);
        }

        public async Task InitializeAsync()
        {
            try
            {
                var instanceId = InstanceDataManager.Instance.InstanceId;
                var report = InstanceDataManager.Instance.CurrentReport;
                if (report == null) return;

                _daemon = InstanceDataManager.Instance.CurrentDaemon;

                if (_daemon == null)
                {
                    Log.Error("[FileManager] Failed to get daemon connection");
                    ShowError("连接失败", "无法获取 Daemon 连接");
                    return;
                }

                _rootPath = $"/instances/{instanceId}";
                CurrentPath = "/";

                TreeItems.Clear();
                var rootItem = new TreeItem(this)
                {
                    Name = "/",
                    VirtualPath = "/",
                    IsExpanded = true
                };
                rootItem.Children.Add(new TreeItem(this) { Name = "Loading..." });
                TreeItems.Add(rootItem);

                await LoadDirectoryAsync(CurrentPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[FileManager] Failed to initialize");
                ShowError("初始化失败", ex.Message);
            }
        }

        private string GetRealPath(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath) || virtualPath == "/")
                return _rootPath;

            if (!virtualPath.StartsWith("/"))
                virtualPath = "/" + virtualPath;

            return _rootPath + virtualPath;
        }

        private string NormalizeVirtualPath(string path)
        {
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var stack = new Stack<string>();
            foreach (var part in parts)
            {
                if (part == ".") continue;
                if (part == "..")
                {
                    if (stack.Count > 0) stack.Pop();
                }
                else
                {
                    stack.Push(part);
                }
            }
            if (stack.Count == 0) return "/";
            var array = stack.ToArray();
            Array.Reverse(array);
            return "/" + string.Join("/", array);
        }

        private string GetParentVirtualPath(string virtualPath)
        {
            if (virtualPath == "/") return "/";
            var lastSlash = virtualPath.LastIndexOf('/');
            if (lastSlash <= 0) return "/";
            return virtualPath.Substring(0, lastSlash);
        }

        public async Task<IEnumerable<Common.ProtoType.Files.DirectoryEntry.DirectoryInformation>> GetDirectoriesAsync(string virtualPath)
        {
            if (_daemon == null) throw new InvalidOperationException("Daemon connection is unavailable.");
            var realPath = GetRealPath(virtualPath);
            var directoryResult = await _daemon.Files.GetDirectoryInfoAsync(new PathRequest(realPath), default);
            if (directoryResult.IsErr(out var directoryError))
                throw DaemonErrorLocalization.ToException(directoryError!);

            return directoryResult.Unwrap().Directories.Select(directory =>
                new Common.ProtoType.Files.DirectoryEntry.DirectoryInformation
                {
                    Name = directory.Name,
                    Meta = new Common.ProtoType.Files.DirectoryMetadata
                    {
                        CreationTime = directory.Meta.CreationTime.ToUnixTimeSeconds(),
                        Hidden = directory.Meta.Hidden,
                        LastAccessTime = directory.Meta.LastAccessTime.ToUnixTimeSeconds(),
                        LastWriteTime = directory.Meta.LastWriteTime.ToUnixTimeSeconds()
                    }
                });
        }

        public void OnTreeItemSelected(TreeItem item)
        {
            if (_isSyncingTree) return;
            _ = NavigateToPathAsync(item.VirtualPath);
        }

        private async Task SyncTreeViewAsync(string virtualPath)
        {
            if (TreeItems.Count == 0) return;
            var current = TreeItems[0]; // Root

            if (virtualPath == "/")
            {
                _isSyncingTree = true;
                current.IsSelected = true;
                _isSyncingTree = false;
                return;
            }

            var parts = virtualPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                current.IsExpanded = true;
                if (!current.IsLoaded)
                {
                    await current.LoadChildrenAsync();
                }

                var next = current.Children.FirstOrDefault(c => c.Name == part);
                if (next == null) break;
                current = next;
            }

            if (current != null)
            {
                _isSyncingTree = true;
                current.IsSelected = true;
                _isSyncingTree = false;
            }
        }

        private async Task LoadDirectoryAsync(string path)
        {
            if (_daemon == null) return;

            try
            {
                HasError = false;

                var virtualPath = path;
                if (!virtualPath.StartsWith("/")) virtualPath = "/" + virtualPath;
                virtualPath = NormalizeVirtualPath(virtualPath);

                var realPath = GetRealPath(virtualPath);
                var directoryResult = await _daemon.Files.GetDirectoryInfoAsync(new PathRequest(realPath), default);
                if (directoryResult.IsErr(out var directoryError))
                    throw DaemonErrorLocalization.ToException(directoryError!);

                var directory = directoryResult.Unwrap();

                Items.Clear();

                if (virtualPath != "/")
                {
                    var parentVirtualPath = GetParentVirtualPath(virtualPath);
                    Items.Add(new FileItem
                    {
                        Name = "..",
                        Path = parentVirtualPath,
                        IsDirectory = true
                    });
                }

                foreach (var dir in directory.Directories)
                {
                    Items.Add(new FileItem
                    {
                        Name = dir.Name,
                        Path = virtualPath == "/" ? $"/{dir.Name}" : $"{virtualPath}/{dir.Name}",
                        IsDirectory = true,
                        ModifiedTime = dir.Meta.LastWriteTime.ToUnixTimeSeconds()
                    });
                }

                foreach (var file in directory.Files)
                {
                    Items.Add(new FileItem
                    {
                        Name = file.Name,
                        Path = virtualPath == "/" ? $"/{file.Name}" : $"{virtualPath}/{file.Name}",
                        IsDirectory = false,
                        SizeBytes = file.Meta.Size,
                        ModifiedTime = file.Meta.LastWriteTime.ToUnixTimeSeconds()
                    });
                }

                CurrentPath = virtualPath;

                if (!_isNavigating)
                {
                    if (_historyIndex < _history.Count - 1)
                    {
                        _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                    }
                    if (_history.Count == 0 || _history[_history.Count - 1] != virtualPath)
                    {
                        _history.Add(virtualPath);
                        _historyIndex++;
                    }
                }

                await SyncTreeViewAsync(virtualPath);

                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[FileManager] Failed to load directory {0}", path);
                ShowError("加载目录失败", ex.Message);
            }
        }

        private async Task OpenItemAsync()
        {
            if (SelectedItem == null || _daemon == null) return;

            if (SelectedItem.IsDirectory)
            {
                await LoadDirectoryAsync(SelectedItem.Path);
            }
            else
            {
                var realPath = GetRealPath(SelectedItem.Path);
                var editor = new Dialogs.FileEditorWindow();
                var vm = new Dialogs.FileEditorViewModel(_daemon, realPath, SelectedItem.Path, SelectedItem.SizeBytes, editor);
                editor.DataContext = vm;

                // Fire and forget loading
                _ = vm.LoadFileAsync();

                editor.Show();
            }
        }

        private async Task DownloadItemAsync()
        {
            if (SelectedItem == null || SelectedItem.IsDirectory || _daemon == null) return;

            var dialog = new SaveFileDialog
            {
                FileName = SelectedItem.Name,
                Filter = "All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var realPath = GetRealPath(SelectedItem.Path);
                    var openResult = await _daemon.Files.OpenDownloadAsync(new DownloadOpenRequest(realPath), default);
                    if (openResult.IsErr(out var openError))
                        throw DaemonErrorLocalization.ToException(openError!);

                    var session = openResult.Unwrap();
                    var completed = false;
                    try
                    {
                        await using var stream = new FileStream(
                            dialog.FileName,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            session.MaxChunkSize,
                            useAsync: true);
                        var offset = 0L;
                        while (true)
                        {
                            var chunkResult = await _daemon.Files.ReadDownloadChunkAsync(
                                new DownloadChunkRequest(session.SessionId, offset, session.MaxChunkSize),
                                default);
                            if (chunkResult.IsErr(out var chunkError))
                                throw DaemonErrorLocalization.ToException(chunkError!);

                            var chunk = chunkResult.Unwrap();
                            if (chunk.Offset != offset)
                                throw new InvalidDataException("The daemon returned a download chunk at an unexpected offset.");

                            await stream.WriteAsync(chunk.Data.AsMemory());
                            offset += chunk.Data.Length;
                            if (chunk.IsFinal)
                                break;
                        }

                        if (offset != session.Length)
                            throw new InvalidDataException("The downloaded file length does not match the daemon metadata.");

                        var closeResult = await _daemon.Files.CloseDownloadAsync(session.SessionId, default);
                        if (closeResult.IsErr(out var closeError))
                            throw DaemonErrorLocalization.ToException(closeError!);

                        completed = true;
                    }
                    finally
                    {
                        if (!completed)
                        {
                            var closeResult = await _daemon.Files.CloseDownloadAsync(session.SessionId, default);
                            if (closeResult.IsErr(out var closeError))
                                Log.Warning("[FileManager] Failed to close download {0}: {1}", session.SessionId, closeError!.Message);
                        }
                    }
                    MessageBox.Show("下载完成！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[FileManager] Failed to download file {0}", SelectedItem.Path);
                    MessageBox.Show($"下载失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task UploadFileAsync()
        {
            if (_daemon == null) return;

            var dialog = new OpenFileDialog
            {
                Multiselect = false,
                Filter = "All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var fileName = Path.GetFileName(dialog.FileName);
                    var virtualTargetPath = CurrentPath == "/" ? $"/{fileName}" : $"{CurrentPath}/{fileName}";
                    var realTargetPath = GetRealPath(virtualTargetPath);

                    await using var stream = File.OpenRead(dialog.FileName);
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                    stream.Position = 0;
                    var openResult = await _daemon.Files.OpenUploadAsync(
                        new UploadOpenRequest(realTargetPath, stream.Length, hash),
                        default);
                    if (openResult.IsErr(out var openError))
                        throw DaemonErrorLocalization.ToException(openError!);

                    var session = openResult.Unwrap();
                    var completed = false;
                    try
                    {
                        var buffer = new byte[session.MaxChunkSize];
                        var offset = 0L;
                        int read;
                        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
                        {
                            var writeResult = await _daemon.Files.WriteUploadChunkAsync(
                                new UploadChunkRequest(session.SessionId, offset, ImmutableArray.Create(buffer[..read])),
                                default);
                            if (writeResult.IsErr(out var writeError))
                                throw DaemonErrorLocalization.ToException(writeError!);

                            offset += read;
                        }

                        var closeResult = await _daemon.Files.CloseUploadAsync(session.SessionId, default);
                        if (closeResult.IsErr(out var closeError))
                            throw DaemonErrorLocalization.ToException(closeError!);

                        completed = true;
                    }
                    finally
                    {
                        if (!completed)
                        {
                            var cancelResult = await _daemon.Files.CancelUploadAsync(session.SessionId, default);
                            if (cancelResult.IsErr(out var cancelError))
                                Log.Warning("[FileManager] Failed to cancel upload {0}: {1}", session.SessionId, cancelError!.Message);
                        }
                    }

                    await LoadDirectoryAsync(CurrentPath);
                    MessageBox.Show("上传完成！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[FileManager] Failed to upload file {0}", dialog.FileName);
                    MessageBox.Show($"上传失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task<string?> ShowInputDialogAsync(string title, string defaultText)
        {
            var textBox = new System.Windows.Controls.TextBox
            {
                Text = defaultText,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var dialog = new iNKORE.UI.WPF.Modern.Controls.ContentDialog
            {
                Title = title,
                Content = textBox,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = iNKORE.UI.WPF.Modern.Controls.ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (result == iNKORE.UI.WPF.Modern.Controls.ContentDialogResult.Primary)
            {
                return textBox.Text;
            }
            return null;
        }

        private async Task RenameItemAsync()
        {
            if (SelectedItem == null || _daemon == null) return;

            string? newName = await ShowInputDialogAsync("重命名", SelectedItem.Name);
            if (string.IsNullOrWhiteSpace(newName) || newName == SelectedItem.Name) return;

            try
            {
                var realPath = GetRealPath(SelectedItem.Path);
                if (SelectedItem.IsDirectory)
                {
                    var renameResult = await _daemon.Files.RenameDirectoryAsync(new PathRenameRequest(realPath, newName), default);
                    if (renameResult.IsErr(out var renameError))
                        throw DaemonErrorLocalization.ToException(renameError!);
                }
                else
                {
                    var renameResult = await _daemon.Files.RenameFileAsync(new PathRenameRequest(realPath, newName), default);
                    if (renameResult.IsErr(out var renameError))
                        throw DaemonErrorLocalization.ToException(renameError!);
                }
                await LoadDirectoryAsync(CurrentPath);

                if (SelectedItem.IsDirectory)
                {
                    var parentPath = GetParentVirtualPath(SelectedItem.Path);
                    await RefreshTreeItemAsync(parentPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[FileManager] Failed to rename {0}", SelectedItem.Path);
                MessageBox.Show($"重命名失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeleteItemAsync()
        {
            if (SelectedItem == null || _daemon == null) return;

            var result = MessageBox.Show($"确定要删除 {SelectedItem.Name} 吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                var realPath = GetRealPath(SelectedItem.Path);
                if (SelectedItem.IsDirectory)
                {
                    var deleteResult = await _daemon.Files.DeleteDirectoryAsync(new DeleteDirectoryRequest(realPath, true), default);
                    if (deleteResult.IsErr(out var deleteError))
                        throw DaemonErrorLocalization.ToException(deleteError!);
                }
                else
                {
                    var deleteResult = await _daemon.Files.DeleteFileAsync(new PathRequest(realPath), default);
                    if (deleteResult.IsErr(out var deleteError))
                        throw DaemonErrorLocalization.ToException(deleteError!);
                }
                await LoadDirectoryAsync(CurrentPath);

                if (SelectedItem.IsDirectory)
                {
                    var parentPath = GetParentVirtualPath(SelectedItem.Path);
                    await RefreshTreeItemAsync(parentPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[FileManager] Failed to delete {0}", SelectedItem.Path);
                MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CreateDirectoryAsync()
        {
            if (_daemon == null) return;

            string? dirName = await ShowInputDialogAsync("新建文件夹", "新建文件夹");
            if (string.IsNullOrWhiteSpace(dirName)) return;

            try
            {
                var virtualNewPath = CurrentPath == "/" ? $"/{dirName}" : $"{CurrentPath}/{dirName}";
                var realNewPath = GetRealPath(virtualNewPath);
                var createResult = await _daemon.Files.CreateDirectoryAsync(new PathRequest(realNewPath), default);
                if (createResult.IsErr(out var createError))
                    throw DaemonErrorLocalization.ToException(createError!);
                await LoadDirectoryAsync(CurrentPath);
                await RefreshTreeItemAsync(CurrentPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[FileManager] Failed to create directory {0}", dirName);
                MessageBox.Show($"创建文件夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RefreshTreeItemAsync(string virtualPath)
        {
            if (TreeItems.Count == 0) return;
            var current = TreeItems[0]; // Root

            if (virtualPath != "/")
            {
                var parts = virtualPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var next = current.Children.FirstOrDefault(c => c.Name == part);
                    if (next == null) return;
                    current = next;
                }
            }

            var dirs = await GetDirectoriesAsync(virtualPath);
            Application.Current.Dispatcher.Invoke(() =>
            {
                current.Children.Clear();
                foreach (var dir in dirs)
                {
                    var child = new TreeItem(this)
                    {
                        Name = dir.Name,
                        VirtualPath = virtualPath == "/" ? $"/{dir.Name}" : $"{virtualPath}/{dir.Name}"
                    };
                    child.Children.Add(new TreeItem(this) { Name = "Loading..." });
                    current.Children.Add(child);
                }
            });
        }

        private void ShowError(string title, string message)
        {
            ErrorTitle = title;
            ErrorMessage = message;
            HasError = true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
