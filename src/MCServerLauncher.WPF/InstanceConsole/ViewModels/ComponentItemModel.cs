using CommunityToolkit.Mvvm.ComponentModel;

namespace MCServerLauncher.WPF.InstanceConsole.ViewModels;

public partial class ComponentItemModel : ObservableObject
{
    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string _virtualPath = string.Empty;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private long _fileSize;
    [ObservableProperty] private ComponentKind _kind;

    private bool _isClientSideOnly;
    public bool IsClientSideOnly
    {
        get => _isClientSideOnly;
        set
        {
            if (SetProperty(ref _isClientSideOnly, value))
            {
                OnPropertyChanged(nameof(BadgeText));
            }
        }
    }

    public string Description => string.IsNullOrEmpty(Version)
        ? FileName
        : $"{FileName} | v{Version}";

    public string Title => string.IsNullOrEmpty(DisplayName) ? FileName : DisplayName;
    public string BadgeText => IsClientSideOnly ? "Client" : string.Empty;

    partial void OnFileNameChanged(string value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
    }
    partial void OnDisplayNameChanged(string value) => OnPropertyChanged(nameof(Title));
    partial void OnVersionChanged(string value) => OnPropertyChanged(nameof(Description));
}

public enum ComponentKind
{
    Mod,
    Plugin
}
