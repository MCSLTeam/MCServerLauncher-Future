using System;

namespace MCServerLauncher.Common.Extensibility;

public interface ISettingsSection
{
    string SectionKey { get; }
    Type ModelType { get; }
    object GetDefaultValue();
}
