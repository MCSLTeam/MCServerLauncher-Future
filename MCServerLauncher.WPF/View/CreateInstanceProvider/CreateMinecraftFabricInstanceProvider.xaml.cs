using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Pages;
using System.Windows;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
{
    /// <summary>
    ///    CreateMinecraftFabricInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateMinecraftFabricInstanceProvider : ICreateInstanceProvider
    {
        public InstanceType InstanceType { get; } = InstanceType.Fabric;
        public TargetType TargetType { get; } = TargetType.Jar;
        public CreateMinecraftFabricInstanceProvider()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    Go back.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void GoPreCreateInstance(object sender, RoutedEventArgs e)
        {
            var parent = this.TryFindParent<CreateInstancePage>();
            parent?.CurrentCreateInstance.GoBack();
        }

        //private void FinishSetup(object sender, RoutedEventArgs e)
        //{
        //}
    }
}