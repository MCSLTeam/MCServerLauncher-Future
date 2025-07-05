using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Pages;
using System.Windows;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
{
    /// <summary>
    ///    CreateMinecraftForgeInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateMinecraftForgeInstanceProvider : ICreateInstanceProvider
    {
        public InstanceType InstanceType { get; } = InstanceType.Forge;
        public TargetType TargetType { get; } = TargetType.Jar;
        public CreateMinecraftForgeInstanceProvider()
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