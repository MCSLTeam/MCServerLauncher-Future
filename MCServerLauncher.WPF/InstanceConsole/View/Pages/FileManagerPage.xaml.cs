using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic;
using System.Windows.Input;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.Modules;
using Microsoft.Win32;
using Serilog;

namespace MCServerLauncher.WPF.InstanceConsole.View.Pages
{
    public partial class FileManagerPage
    {
        private FileManagerViewModel _viewModel;

        public FileManagerPage()
        {
            InitializeComponent();
            _viewModel = new FileManagerViewModel(this);
            DataContext = _viewModel;
            Loaded += FileManagerPage_Loaded;
        }

        public void ShowError(string title, string message)
        {
            StopTipLayer.Symbol = "❌";
            StopTipLayer.StopTip = title;
            StopTipLayer.StopDescription = message;
            StopTipLayer.ButtonIcon = iNKORE.UI.WPF.Modern.Common.IconKeys.SegoeFluentIcons.Refresh;
            StopTipLayer.ButtonText = Lang.Tr["Refresh"];
            StopTipLayer.Visibility = Visibility.Visible;
            MainContentGrid.Visibility = Visibility.Collapsed;
        }

        public void HideError()
        {
            StopTipLayer.Visibility = Visibility.Collapsed;
            MainContentGrid.Visibility = Visibility.Visible;
        }

        private async void FileManagerPage_Loaded(object sender, RoutedEventArgs e)
        {
            await _viewModel.InitializeAsync();
        }

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
        private readonly FileManagerPage _page;
        private IDaemon? _daemon;
        private string _rootPath = "";
        private string _currentPath = "";
        private FileItem? _selectedItem;
        private ObservableCollection<FileItem> _items = new();
        private ObservableCollection<TreeItem> _treeItems = new();
        private List<string> _history = new();
        private int _historyIndex = -1;
        private bool _isNavigating = false;
        private bool _isSyncingTree = false;

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

        public FileManagerViewModel(FileManagerPage page)
        {
            _page = page;
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

                var daemonField = typeof(InstanceDataManager).GetField("_daemon", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (daemonField != null)
                {
                    _daemon = daemonField.GetValue(InstanceDataManager.Instance) as IDaemon;
                }

                if (_daemon == null)
                {
                    Log.Error("[FileManager] Failed to get daemon connection");
                    _page.ShowError("连接失败", "无法获取 Daemon 连接");
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
                _page.ShowError("初始化失败", ex.Message);
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

        public async Task<IEnumerable<MCServerLauncher.Common.ProtoType.Files.DirectoryEntry.DirectoryInformation>> GetDirectoriesAsync(string virtualPath)
        {
            if (_daemon == null) return Array.Empty<MCServerLauncher.Common.ProtoType.Files.DirectoryEntry.DirectoryInformation>();
            var realPath = GetRealPath(virtualPath);
            var (directories, _, _) = await _daemon.GetDirectoryInfoAsync(realPath);
            return directories;
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
                _page.HideError();
                
                var virtualPath = path;
                if (!virtualPath.StartsWith("/")) virtualPath = "/" + virtualPath;
                virtualPath = NormalizeVirtualPath(virtualPath);
                
                var realPath = GetRealPath(virtualPath);
                var (directories, files, parent) = await _daemon.GetDirectoryInfoAsync(realPath);
                
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

                foreach (var dir in directories)
                {
                    Items.Add(new FileItem
                    {
                        Name = dir.Name,
                        Path = virtualPath == "/" ? $"/{dir.Name}" : $"{virtualPath}/{dir.Name}",
                        IsDirectory = true,
                        ModifiedTime = dir.Meta.LastWriteTime
                    });
                }

                foreach (var file in files)
                {
                    Items.Add(new FileItem
                    {
                        Name = file.Name,
                        Path = virtualPath == "/" ? $"/{file.Name}" : $"{virtualPath}/{file.Name}",
                        IsDirectory = false,
                        SizeBytes = file.Meta.Size,
                        ModifiedTime = file.Meta.LastWriteTime
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
                _page.ShowError("加载目录失败", ex.Message);
            }
        }

        private async Task OpenItemAsync()
        {
            if (SelectedItem == null) return;

            if (SelectedItem.IsDirectory)
            {
                await LoadDirectoryAsync(SelectedItem.Path);
            }
            else
            {
                MessageBox.Show("暂不支持直接打开文件，请先下载。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    var context = await _daemon.DownloadFileAsync(realPath, dialog.FileName, 1024 * 1024); // 1MB chunks
                    // TODO: Show progress UI
                    await context.NetworkLoadTask;
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
                    
                    var context = await _daemon.UploadFileAsync(dialog.FileName, realTargetPath, 1024 * 1024); // 1MB chunks
                    // TODO: Show progress UI
                    await context.NetworkLoadTask;
                    
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
                Margin = new System.Windows.Thickness(0, 10, 0, 0)
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
                    await _daemon.RenameDirectoryAsync(realPath, newName);
                }
                else
                {
                    await _daemon.RenameFileAsync(realPath, newName);
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
                    await _daemon.DeleteDirectoryAsync(realPath, true);
                }
                else
                {
                    await _daemon.DeleteFileAsync(realPath);
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
                await _daemon.CreateDirectoryAsync(realNewPath);
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

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}