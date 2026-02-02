# xaml.cs 逻辑迁移到 ViewModel 指南

## 概述

本文档说明如何将现有 View 代码后置（xaml.cs）中的业务逻辑迁移到对应的 ViewModel，实现真正的 MVVM 分离。

## 已完成示例

### ✅ SettingsPage - 完整迁移示例

**迁移前**: 572行代码
- ~500行 DependencyProperty 定义
- ~15个事件处理器
- 大量初始化和业务逻辑

**迁移后**: 44行代码
- 只保留构造函数和UI相关代码
- 所有业务逻辑在 SettingsPageViewModel
- 92% 代码减少

**文件对比**:
```csharp
// 迁移前 - SettingsPage.xaml.cs (572行)
public partial class SettingsPage
{
    // 大量 DependencyProperty
    public bool MinecraftJavaAutoAcceptEula { get; set; }
    public static readonly DependencyProperty MinecraftJavaAutoAcceptEulaProperty = ...
    
    // 大量事件处理器
    private void OnMinecraftJavaAutoAcceptEulaChanged(object sender, RoutedEventArgs e)
    {
        SettingsManager.SaveSetting("InstanceCreation.MinecraftJavaAutoAcceptEula", ...);
    }
    
    // 初始化代码
    InstanceCreation_MinecraftJavaAutoAgreeEula.SettingSwitch.Toggled += ...
    // ... 500+ 行类似代码
}

// 迁移后 - SettingsPage.xaml.cs (44行)
public partial class SettingsPage
{
    public SettingsPage(SettingsPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
    
    // 仅保留UI相关逻辑
    private void CheckDebugMode(...) { /* UI only */ }
}

// SettingsPageViewModel.cs (新建)
public partial class SettingsPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _minecraftJavaAutoAcceptEula;
    
    partial void OnMinecraftJavaAutoAcceptEulaChanged(bool value)
    {
        _settingsService.SaveSetting("InstanceCreation.MinecraftJavaAutoAcceptEula", value);
    }
}
```

## 迁移步骤模板

### 第1步：识别需要迁移的逻辑

在 xaml.cs 中查找：
- ❌ **应该迁移**：
  - 事件处理器（Click, SelectionChanged, ValueChanged 等）
  - 业务逻辑（数据验证、计算、API调用）
  - 数据获取和处理
  - DependencyProperty（用于业务数据）
  - 状态管理
  
- ✅ **可以保留**：
  - 纯UI动画（如淡入淡出）
  - UI特定的行为（如滚动、焦点管理）
  - 设计时支持代码
  - DataContext 设置

### 第2步：在 ViewModel 中实现逻辑

#### 模式 1: Event Handler → RelayCommand

**迁移前 (xaml.cs)**:
```csharp
private async void RefreshButton_Click(object sender, RoutedEventArgs e)
{
    ShowLoading();
    var data = await _api.GetData();
    UpdateUI(data);
    HideLoading();
}
```

**迁移后 (ViewModel)**:
```csharp
[ObservableProperty]
private bool _isLoading;

[ObservableProperty]
private ObservableCollection<DataItem> _items;

[RelayCommand]
private async Task Refresh()
{
    IsLoading = true;
    try
    {
        var data = await _api.GetData();
        Items = new ObservableCollection<DataItem>(data);
    }
    finally
    {
        IsLoading = false;
    }
}
```

**XAML 更新**:
```xml
<!-- 迁移前 -->
<Button Content="Refresh" Click="RefreshButton_Click"/>

<!-- 迁移后 -->
<Button Content="Refresh" Command="{Binding RefreshCommand}"/>
<ProgressBar Visibility="{Binding IsLoading, Converter={StaticResource BoolToVisibility}}"/>
```

#### 模式 2: DependencyProperty → ObservableProperty

**迁移前 (xaml.cs)**:
```csharp
public int ThreadCount
{
    get => (int)GetValue(ThreadCountProperty);
    set => SetValue(ThreadCountProperty, value);
}

public static readonly DependencyProperty ThreadCountProperty =
    DependencyProperty.Register(
        nameof(ThreadCount),
        typeof(int),
        typeof(MyPage),
        new PropertyMetadata(16, OnThreadCountChanged)
    );

private static void OnThreadCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
{
    SettingsManager.SaveSetting("ThreadCount", (int)e.NewValue);
}
```

**迁移后 (ViewModel)**:
```csharp
[ObservableProperty]
private int _threadCount = 16;

partial void OnThreadCountChanged(int value)
{
    _settingsService.SaveSetting("ThreadCount", value);
}
```

#### 模式 3: Collection 管理

**迁移前 (xaml.cs)**:
```csharp
private void LoadItems()
{
    ItemsContainer.Items.Clear();
    foreach (var item in items)
    {
        var card = new ItemCard { Data = item };
        ItemsContainer.Items.Add(card);
    }
}
```

**迁移后 (ViewModel)**:
```csharp
[ObservableProperty]
private ObservableCollection<ItemViewModel> _items = new();

public void LoadItems()
{
    Items.Clear();
    foreach (var item in _service.GetItems())
    {
        Items.Add(new ItemViewModel(item));
    }
}
```

**XAML 更新**:
```xml
<!-- 迁移前 -->
<ItemsControl x:Name="ItemsContainer"/>

<!-- 迁移后 -->
<ItemsControl ItemsSource="{Binding Items}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <local:ItemCard/>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

#### 模式 4: 对话框处理

**迁移前 (xaml.cs)**:
```csharp
private async void AddButton_Click(object sender, RoutedEventArgs e)
{
    var dialog = new AddItemDialog();
    var result = await dialog.ShowAsync();
    if (result == ContentDialogResult.Primary)
    {
        var item = dialog.GetItem();
        AddItem(item);
    }
}
```

**迁移后 (ViewModel + Service)**:
```csharp
// IDialogService.cs
public interface IDialogService
{
    Task<(bool success, T data)> ShowDialog<T>(DialogViewModel<T> viewModel);
}

// ViewModel
[RelayCommand]
private async Task AddItem()
{
    var (success, item) = await _dialogService.ShowDialog(new AddItemDialogViewModel());
    if (success)
    {
        Items.Add(item);
    }
}
```

### 第3步：更新 View 代码

**简化 xaml.cs**:
```csharp
public partial class MyPage
{
    public MyPage(MyPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        
        // 只保留必要的UI初始化
    }
    
    // 只保留纯UI逻辑（如动画）
}
```

### 第4步：验证构建

```bash
dotnet build /p:EnableWindowsTargeting=true
```

## 待迁移页面清单

### 高优先级

#### ✅ SettingsPage (完成)
- 代码减少: 572行 → 44行
- ViewModel: 完整实现

#### ⏳ ResDownloadPage (102行)
**需要迁移的逻辑**:
```csharp
// xaml.cs 中的方法
- Refresh(object sender, RoutedEventArgs e)
- ToggleResDownloadProvider()
- ShowLoadingLayer() / HideLoadingLayer()
```

**迁移策略**:
```csharp
// ViewModel
[ObservableProperty] private bool _isLoading;
[ObservableProperty] private string _currentProvider;
[ObservableProperty] private IResDownloadProvider _provider;

[RelayCommand]
private async Task Refresh()
{
    IsLoading = true;
    Provider = GetProviderByName(CurrentProvider);
    await Provider.Refresh();
    IsLoading = false;
}
```

#### ⏳ CreateInstancePage (165行)
**需要迁移的逻辑**:
```csharp
// xaml.cs 中的方法
- ValidateFuncAvailable()
- ShowNoDaemonLayer()
- NewMinecraftJavaServerPage() 等工厂方法
- SelectDaemon() 对话框
```

**迁移策略**:
```csharp
// ViewModel
[ObservableProperty] private bool _hasDaemon;
[ObservableProperty] private bool _showNoDaemonWarning;

[RelayCommand]
private async Task CreateInstance(string instanceType)
{
    if (!HasDaemon)
    {
        ShowNoDaemonWarning = true;
        return;
    }
    
    var daemon = await SelectDaemon();
    // ...
}
```

#### ⏳ DaemonManagerPage (136行)
**需要迁移的逻辑**:
```csharp
// xaml.cs 中的方法
- AddDaemonConnection(object sender, RoutedEventArgs e)
- EditDaemonConnection(...)
- TryConnectDaemon(...)
- IsVisibleChanged 中的卡片创建
```

**迁移策略**:
```csharp
// ViewModel
[ObservableProperty]
private ObservableCollection<DaemonCardViewModel> _daemons = new();

[RelayCommand]
private async Task AddDaemon()
{
    var (success, config) = await _dialogService.ShowDaemonDialog();
    if (success)
    {
        var daemon = new DaemonCardViewModel(config);
        await daemon.Connect();
        if (daemon.IsConnected)
        {
            Daemons.Add(daemon);
            _daemonService.Save(config);
        }
    }
}

public async Task LoadDaemons()
{
    Daemons.Clear();
    var configs = _daemonService.GetAll();
    var tasks = configs.Select(async c =>
    {
        var vm = new DaemonCardViewModel(c);
        await vm.Connect();
        return vm;
    });
    var results = await Task.WhenAll(tasks);
    foreach (var daemon in results)
    {
        Daemons.Add(daemon);
    }
}
```

### 中优先级

#### ⏳ InstanceManagerPage (499行) - 最复杂
**需要迁移的逻辑**:
- 实例卡片管理
- 过滤和搜索
- 批量操作
- 刷新逻辑

**建议**: 分多个阶段迁移，先迁移基础功能

### 低优先级

- FirstSetupHelper 各页面
- Provider 各组件
- Debug页面

## 迁移检查清单

对每个页面完成迁移后，检查：

- [ ] ✅ code-behind 只保留构造函数和UI逻辑
- [ ] ✅ 所有业务逻辑在 ViewModel
- [ ] ✅ XAML 使用 Command 绑定替代 Click
- [ ] ✅ 使用 ObservableProperty 替代 DependencyProperty
- [ ] ✅ 构建成功无错误
- [ ] ✅ 功能测试通过
- [ ] ✅ 代码行数明显减少

## 常见问题

### Q: 如何处理需要访问 UI 元素的代码？
A: 考虑是否真的需要。如果确实需要，可以：
1. 使用 Attached Behavior
2. 使用 Blend Interactivity
3. 创建自定义控件
4. 保留在 View（仅限纯UI操作）

### Q: 事件参数怎么办（如 EventArgs）？
A: RelayCommand 支持参数：
```csharp
[RelayCommand]
private void ItemSelected(object parameter)
{
    if (parameter is Item item)
    {
        // ...
    }
}
```

### Q: 现有的 x:Name 引用怎么办？
A: 
1. 优先使用数据绑定替代
2. 必要时保留 x:Name，但在 ViewModel 中通过属性公开数据
3. 逐步重构，不要一次性全部改

### Q: 如何处理复杂的初始化逻辑？
A: 将初始化移到 ViewModel：
```csharp
public MyPageViewModel(IService service)
{
    _service = service;
    Initialize();
}

private void Initialize()
{
    // 初始化逻辑
    LoadSettings();
    SetupCollections();
}
```

## 最佳实践

1. **增量迁移**: 一次迁移一个页面，确保每次都能构建成功
2. **保持功能**: 迁移过程中不改变功能
3. **测试驱动**: 迁移前后都要测试功能
4. **代码审查**: 确保 View 不包含业务逻辑
5. **文档记录**: 更新相关文档

## 相关资源

- MVVM_MIGRATION_GUIDE.md - 总体迁移指南
- COMPONENTS_PROVIDERS_MVVM.md - Components & Providers 指南
- CommunityToolkit.Mvvm 文档: https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/

---

**更新日期**: 2026-02-02
**状态**: SettingsPage 完成，其他页面进行中
