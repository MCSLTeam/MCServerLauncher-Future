using System;
using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    SelectMinecraftBedrockArchive.xaml 的交互逻辑
    /// </summary>
    public partial class SelectMinecraftBedrockArchive
    {
        public SelectMinecraftBedrockArchive()
        {
            InitializeComponent();
        }

        public CreateInstanceData ActualData {
            get => new() { 
                Type = CreateInstanceDataType.Path,
                Data = ArchiveTextBox.Text,
            };
        }
    }
}