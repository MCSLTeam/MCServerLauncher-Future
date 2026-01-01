using System.Threading.Tasks;

namespace MCServerLauncher.WPF.InstanceConsole.View.Components
{
    /// <summary>
    /// Instance console board component interface.
    /// All board components should implement this interface for standardized data loading and refresh.
    /// </summary>
    public interface IInstanceBoardComponent
    {
        /// <summary>
        /// Initialize component with instance data
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Refresh component data from daemon
        /// </summary>
        Task RefreshAsync();

        /// <summary>
        /// Indicates whether the component is currently loading data
        /// </summary>
        bool IsLoading { get; }

        /// <summary>
        /// Indicates whether the component has encountered an error
        /// </summary>
        bool HasError { get; }
    }
}
