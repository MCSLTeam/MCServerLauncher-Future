using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services.Interfaces;

namespace MCServerLauncher.WPF.Services
{
    /// <summary>
    /// Service implementation for managing application settings.
    /// </summary>
    public class SettingsService : ISettingsService
    {
        public SettingsManager.Settings CurrentSettings => SettingsManager.Get!;

        public void Initialize()
        {
            SettingsManager.InitSettings();
            // Initialize legacy static wrapper for backward compatibility
#pragma warning disable CS0618 // Type or member is obsolete
            SettingsManagerLegacy.Initialize(this);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public void SaveSetting<T>(string settingPath, T value)
        {
            SettingsManager.SaveSetting(settingPath, value);
        }

        public void SaveAll()
        {
            // The current implementation saves automatically through the queue
            // This method can be used to force an immediate save if needed
            // For now, we rely on the existing queue mechanism
        }
    }
}
