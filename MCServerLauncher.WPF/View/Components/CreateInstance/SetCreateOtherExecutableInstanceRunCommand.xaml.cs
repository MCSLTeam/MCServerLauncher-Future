using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    SetCreateOtherExecutableInstanceRunCommand.xaml 的交互逻辑
    /// </summary>
    public partial class SetCreateOtherExecutableInstanceRunCommand
    {
        public SetCreateOtherExecutableInstanceRunCommand()
        {
            InitializeComponent();
        }

        public CreateInstanceData ActualData
        {
            get => new()
            {
                Type = CreateInstanceDataType.CommandLine,
                Data = RunCommandTextBox.Text,
            };
        }
    }
}