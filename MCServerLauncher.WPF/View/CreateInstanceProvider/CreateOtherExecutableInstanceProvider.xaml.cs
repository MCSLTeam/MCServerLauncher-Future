using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.WPF.View.Pages;
using System.Windows;
using static MCServerLauncher.WPF.Modules.VisualTreeHelper;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
{
    /// <summary>
    ///    CreateOtherExecutableInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateOtherExecutableInstanceProvider : ICreateInstanceProvider
    {
        public InstanceType InstanceType { get; } = InstanceType.None;
        public TargetType TargetType { get; } = TargetType.Executable;
        public CreateOtherExecutableInstanceProvider()
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