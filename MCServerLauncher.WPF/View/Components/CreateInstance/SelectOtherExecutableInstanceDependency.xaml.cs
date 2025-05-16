using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    SelectOtherExecutableInstanceDependency.xaml 的交互逻辑
    /// </summary>
    public partial class SelectOtherExecutableInstanceDependency
    {
        public SelectOtherExecutableInstanceDependency()
        {
            InitializeComponent();
        }

        public CreateInstanceData ActualData
        {
            get => new()
            {
                Type = CreateInstanceDataType.Path,
                Data = DepsTextBox.Text,
            };
        }
    }
}