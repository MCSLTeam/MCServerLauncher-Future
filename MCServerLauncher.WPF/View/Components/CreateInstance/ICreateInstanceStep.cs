using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    interface ICreateInstanceStep
    {
        public bool IsFinished { get; }
        public CreateInstanceData ActualData { get; }
    }
}
