using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    SelectTerrariaArchive.xaml 的交互逻辑
    /// </summary>
    public partial class SelectTerrariaArchive
    {
        public SelectTerrariaArchive()
        {
            InitializeComponent();
        }

        public CreateInstanceData ActualData
        {
            get => new()
            {
                Type = CreateInstanceDataType.Path,
                Data = TerrariaExeTextBox.Text,
            };
        }
    }
}