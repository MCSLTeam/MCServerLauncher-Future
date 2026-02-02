# Components & Providers MVVM 适配总结

## 概述

根据新需求，已为 View 中的所有 Components 和 Helper/Provider 创建了对应的 ViewModels，并在 DI 容器中注册。

## 新增的 ViewModels

### 1. FirstSetupHelper (首次设置向导) - 5个

这些 ViewModel 用于应用程序首次启动时的设置向导：

| ViewModel | 用途 | 文件位置 |
|-----------|------|----------|
| `FirstSetupViewModel` | 设置向导主控制器 | ViewModels/FirstSetupHelper/FirstSetupViewModel.cs |
| `WelcomeSetupPageViewModel` | 欢迎页面 | ViewModels/FirstSetupHelper/WelcomeSetupPageViewModel.cs |
| `LanguageSetupPageViewModel` | 语言选择页面 | ViewModels/FirstSetupHelper/LanguageSetupPageViewModel.cs |
| `EulaSetupPageViewModel` | EULA 协议同意页面 | ViewModels/FirstSetupHelper/EulaSetupPageViewModel.cs |
| `DaemonSetupPageViewModel` | Daemon 连接设置 | ViewModels/FirstSetupHelper/DaemonSetupPageViewModel.cs |

### 2. PreCreateInstance Providers - 2个

用于实例创建前的准备工作：

| ViewModel | 用途 | 文件位置 |
|-----------|------|----------|
| `PreCreateInstanceViewModel` | 实例类型选择 | ViewModels/Providers/PreCreateInstanceViewModel.cs |
| `PreCreateMinecraftInstanceViewModel` | Minecraft 实例创建前置 | ViewModels/Providers/PreCreateMinecraftInstanceViewModel.cs |

### 3. ResDownload Providers (资源下载提供程序) - 5个

用于不同下载源的资源获取：

| ViewModel | 用途 | 文件位置 |
|-----------|------|----------|
| `FastMirrorProviderViewModel` | FastMirror 下载源 | ViewModels/Providers/FastMirrorProviderViewModel.cs |
| `PolarsMirrorProviderViewModel` | PolarsMirror 下载源 | ViewModels/Providers/PolarsMirrorProviderViewModel.cs |
| `MCSLSyncProviderViewModel` | MCSLSync 下载源 | ViewModels/Providers/MCSLSyncProviderViewModel.cs |
| `MSLAPIProviderViewModel` | MSLAPI 下载源 | ViewModels/Providers/MSLAPIProviderViewModel.cs |
| `RainYunProviderViewModel` | RainYun 下载源 | ViewModels/Providers/RainYunProviderViewModel.cs |

### 4. CreateInstance Providers (实例创建提供程序) - 8个

用于创建不同类型的服务器实例：

| ViewModel | 用途 | 文件位置 |
|-----------|------|----------|
| `CreateMinecraftJavaInstanceProviderViewModel` | Minecraft Java 服务器 | ViewModels/Providers/CreateMinecraftJavaInstanceProviderViewModel.cs |
| `CreateMinecraftForgeInstanceProviderViewModel` | Minecraft Forge 服务器 | ViewModels/Providers/CreateMinecraftForgeInstanceProviderViewModel.cs |
| `CreateMinecraftNeoForgeInstanceProviderViewModel` | Minecraft NeoForge 服务器 | ViewModels/Providers/CreateMinecraftNeoForgeInstanceProviderViewModel.cs |
| `CreateMinecraftFabricInstanceProviderViewModel` | Minecraft Fabric 服务器 | ViewModels/Providers/CreateMinecraftFabricInstanceProviderViewModel.cs |
| `CreateMinecraftQuiltInstanceProviderViewModel` | Minecraft Quilt 服务器 | ViewModels/Providers/CreateMinecraftQuiltInstanceProviderViewModel.cs |
| `CreateMinecraftBedrockInstanceProviderViewModel` | Minecraft Bedrock 服务器 | ViewModels/Providers/CreateMinecraftBedrockInstanceProviderViewModel.cs |
| `CreateTerrariaInstanceProviderViewModel` | Terraria 服务器 | ViewModels/Providers/CreateTerrariaInstanceProviderViewModel.cs |
| `CreateOtherExecutableInstanceProviderViewModel` | 其他可执行程序 | ViewModels/Providers/CreateOtherExecutableInstanceProviderViewModel.cs |

## DI 容器注册

所有 20 个 ViewModel 已在 `App.xaml.cs` 的 `ConfigureServices` 方法中注册为 Transient（瞬态）：

```csharp
// FirstSetupHelper ViewModels
services.AddTransient<FirstSetupViewModel>();
services.AddTransient<WelcomeSetupPageViewModel>();
services.AddTransient<LanguageSetupPageViewModel>();
services.AddTransient<EulaSetupPageViewModel>();
services.AddTransient<DaemonSetupPageViewModel>();

// Provider ViewModels
services.AddTransient<PreCreateInstanceViewModel>();
services.AddTransient<PreCreateMinecraftInstanceViewModel>();
services.AddTransient<FastMirrorProviderViewModel>();
// ... 等等
```

## ViewModel 结构

所有 ViewModel 都遵循相同的结构：

```csharp
using MCServerLauncher.WPF.ViewModels.Base;

namespace MCServerLauncher.WPF.ViewModels.[Category]
{
    /// <summary>
    /// ViewModel for [ComponentName].
    /// TODO: Implement [specific] logic
    /// </summary>
    public partial class [ComponentName]ViewModel : ViewModelBase
    {
        public [ComponentName]ViewModel()
        {
        }
    }
}
```

特点：
- 继承自 `ViewModelBase`（提供 `INotifyPropertyChanged`）
- 使用 `partial class` 支持代码生成器
- 支持构造函数注入依赖服务
- 可使用 `[ObservableProperty]` 和 `[RelayCommand]` 特性

## 下一步工作

虽然 ViewModel 基础架构已完成，但还需要：

### 1. 更新 View 代码使用构造函数注入

**现状**（以 FirstSetup.xaml.cs 为例）：
```csharp
public partial class FirstSetup
{
    private readonly Page _language = new LanguageSetupPage();
    // ...
    
    public FirstSetup()
    {
        InitializeComponent();
        CurrentPage.Navigate(_language);
    }
}
```

**目标**：
```csharp
public partial class FirstSetup
{
    public FirstSetup(FirstSetupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        // Navigation logic moved to ViewModel
    }
}
```

### 2. 将业务逻辑迁移到 ViewModel

需要将以下逻辑从 View 代码迁移到 ViewModel：
- 事件处理逻辑
- 数据获取和处理
- 状态管理
- 验证逻辑

### 3. 更新 XAML 使用数据绑定

将事件绑定改为命令绑定：

**Before**:
```xml
<Button Content="Next" Click="OnNextClicked"/>
```

**After**:
```xml
<Button Content="Next" Command="{Binding NextCommand}"/>
```

## 需要注意的组件

### 复杂组件优先级

以下组件较为复杂，建议优先处理：

1. **FirstSetup** - 控制整个设置流程
2. **PreCreateInstance** - 实例类型选择和 Daemon 选择
3. **Create*InstanceProvider** - 各种实例创建逻辑
4. **ResDownloadProvider** - 资源下载逻辑

### Generic Components

`View/Components/Generic` 下的组件（如 LoadingScreen, NotificationContainer 等）通常是无状态的 UI 组件，可能不需要复杂的 ViewModel，视情况而定。

## 构建状态

✅ **所有新增 ViewModel 编译通过**
- 0 错误
- 0 警告（与新增代码相关）

## 参考示例

可参考已完成的 `HomePageViewModel` 来实现其他 ViewModel：

```csharp
public partial class HomePageViewModel : ViewModelBase
{
    private readonly INotificationService _notificationService;
    
    public HomePageViewModel(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }
    
    [RelayCommand]
    private void ShowConsoleWindow()
    {
        new ConsoleWindow().Show();
    }
    
    [RelayCommand]
    private void PushNotification(string parameter)
    {
        // Command logic
        _notificationService.Push(/* ... */);
    }
}
```

## 总结

- ✅ 20 个新 ViewModel 已创建
- ✅ 所有 ViewModel 已注册到 DI 容器
- ✅ 构建成功，无错误
- 🔄 下一步：更新 View 代码使用构造函数注入
- 🔄 下一步：实现 ViewModel 中的业务逻辑

---

**创建日期**: 2026-02-02
**状态**: ViewModel 基础架构完成，待实现具体逻辑
