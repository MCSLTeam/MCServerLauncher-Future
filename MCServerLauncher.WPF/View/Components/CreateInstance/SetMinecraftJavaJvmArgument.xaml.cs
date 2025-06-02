using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.View.Components.DaemonManager;
using System;
using System.Windows;
using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    SetMinecraftJavaJvmArgument.xaml 的交互逻辑
    /// </summary>
    public partial class SetMinecraftJavaJvmArgument
    {
        public SetMinecraftJavaJvmArgument()
        {
            InitializeComponent();
        }

        private void AddJvmArgument(object sender, RoutedEventArgs e)
        {
            ArgsListView.Items.Add(new JvmArgumentItem());
        }


        private string[] GetAllArgs()
        {
            var args = new string[ArgsListView.Items.Count];
            for (var i = 0; i < ArgsListView.Items.Count; i++)
            {
                var item = (JvmArgumentItem)ArgsListView.Items[i];
                args[i] = item.Argument;
            }
            return args;
        }
        public CreateInstanceData ActualData
        {
            get => new()
            {
                Type = CreateInstanceDataType.List,
                Data = GetAllArgs(),
            };
        }
        private async void ShowArgHelper(object sender, RoutedEventArgs e)
        {
            (ContentDialog dialog, JvmArgHelper argHelper) = await Utils.ConstructJvmArgHelperDialog();
            //dialog.PrimaryButtonClick += (o, args) => null;
            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }
}