using System.Threading.Tasks;

namespace MCServerLauncher.WPF.View.ResDownloadProvider
{
    internal interface IResDownloadProvider
    {
        string ResProviderName { get; }
        Task<bool> Refresh();
    }
}
