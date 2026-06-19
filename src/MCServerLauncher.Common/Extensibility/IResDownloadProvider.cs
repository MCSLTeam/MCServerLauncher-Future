using System.Threading.Tasks;

namespace MCServerLauncher.Common.Extensibility;

public interface IResDownloadProvider
{
    string Id { get; }
    string DisplayName { get; }
    Task<bool> RefreshAsync();
}
