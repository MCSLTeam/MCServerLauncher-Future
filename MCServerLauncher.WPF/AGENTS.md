# MCServerLauncher-Future/MCServerLauncher.WPF Guide

Guide for agents working on the MCServerLauncher.WPF project.

## Project Overview

**MCServerLauncher.WPF** is the graphical user interface client for the MCServerLauncher-Future suite. It connects to one or more Daemons to manage Minecraft server instances remotely.

- **Repository**: MCServerLauncher-Future
- **Language**: C# (.NET 10.0)
- **UI Framework**: WPF (Windows Presentation Foundation)
- **Architecture**: Client (Connects to Daemon via WebSocket)

## Essential Commands

```bash
# Run the WPF Client
dotnet run --project MCServerLauncher.WPF/MCServerLauncher.WPF.csproj

# Build the WPF Client
dotnet build MCServerLauncher.WPF/MCServerLauncher.WPF.csproj
```

## Project Structure

```
MCServerLauncher.WPF/
├── Translations/    # Contains .resx files for internationalization (i18n)
├── Modules/         # Core logic modules (e.g., Initializer.cs, Language.cs, Settings.cs)
├── View/            # WPF XAML pages and components
├── Resources/       # Static assets like images and fonts
└── App.xaml         # Application entry point and global resources
```

## Key Concepts

- **WPF Client**: A graphical interface that connects to one or more Daemons to manage instances remotely.
- **UI Framework**: The project uses **iNKORE.UI.WPF.Modern** (based on ModernWPF) for its UI components.
  - Always use `xmlns:ui="http://schemas.inkore.net/lib/ui/wpf/modern"` in XAML files.
  - When using standard WPF controls like `ListView` with `GridView`, be aware that ModernWPF applies implicit styles that may override default behaviors. For example, to customize `ListViewItem` in a `GridView`, you must base your style on the GridView's specific key: `BasedOn="{StaticResource {x:Static GridView.GridViewItemContainerStyleKey}}"`.
  - Use `ui:FontIcon` for icons (e.g., `<ui:FontIcon Glyph="&#xE8B7;" />` or `<ui:FontIcon Icon="{x:Static ui:SegoeFluentIcons.Refresh}" />`).
- **i18n**: The WPF client supports multiple languages. Always use `Lang.Tr("Key")` for UI text and ensure keys are present in the `.resx` files (especially `Lang.zh-CN.resx` and `Lang.en-US.resx`).
- **Path Handling**: Use `AppDomain.CurrentDomain.BaseDirectory` for absolute paths to ensure consistency regardless of how the application is launched.
- **MVVM/Code-Behind**: The project uses a mix of XAML for UI and C# code-behind for logic.

## Naming Conventions & Code Style

**C#**:

- Use PascalCase for classes, methods, properties, and events.
- Use camelCase for local variables and method parameters.
- Use `_camelCase` for private fields.

## Git & Workflow Notes

- Ensure all tests pass and code compiles before committing.
- When adding new UI text, always update the translation files.

## References

- [.NET Documentation](https://learn.microsoft.com/en-us/dotnet/)
- [WPF Documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
