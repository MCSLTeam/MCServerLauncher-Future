using MCServerLauncher.WPF.Modules;

namespace MCServerLauncher.WPF.Services.Interfaces
{
    /// <summary>
    /// Service for managing application settings.
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>
        /// Gets the current settings.
        /// </summary>
        SettingsManager.Settings CurrentSettings { get; }

        /// <summary>
        /// Initializes the settings service.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Saves a specific setting.
        /// </summary>
        /// <typeparam name="T">Type of the setting value.</typeparam>
        /// <param name="settingPath">The setting path in the format 'Class.Property'.</param>
        /// <param name="value">The value to save.</param>
        void SaveSetting<T>(string settingPath, T value);

        /// <summary>
        /// Saves all settings to disk immediately.
        /// </summary>
        void SaveAll();
    }
}
