using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    SetInstanceName.xaml 的交互逻辑
    /// </summary>
    public partial class SetInstanceName
    {
        public SetInstanceName()
        {
            InitializeComponent();
        }

        public CreateInstanceData ActualData
        {
            get => new()
            {
                Type = CreateInstanceDataType.String,
                Data = ServerNameSetting.Text,
            };
        }
    }
}