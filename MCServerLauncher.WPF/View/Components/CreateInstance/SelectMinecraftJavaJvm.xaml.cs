using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    SelectMinecraftJavaJvm.xaml 的交互逻辑
    /// </summary>
    public partial class SelectMinecraftJavaJvm
    {
        public SelectMinecraftJavaJvm()
        {
            InitializeComponent();
        }

        public CreateInstanceData ActualData
        {
            get => new()
            {
                Type = CreateInstanceDataType.Path,
                Data = JavaRuntimeTextBox.Text,
            };
        }
    }
}