using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    SelectMinecraftJavaCore.xaml 的交互逻辑
    /// </summary>
    public partial class SelectMinecraftJavaCore
    {
        public SelectMinecraftJavaCore()
        {
            InitializeComponent();
        }

        public CreateInstanceData ActualData
        {
            get => new()
            {
                Type = CreateInstanceDataType.Path,
                Data = CoreTextBox.Text,
            };
        }
    }
}