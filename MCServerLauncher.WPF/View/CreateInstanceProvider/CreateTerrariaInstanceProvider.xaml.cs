using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.WPF.View.Pages;
using System.Windows;
using static MCServerLauncher.WPF.Modules.VisualTreeHelper;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
{
    /// <summary>
    ///    CreateTerrariaInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateTerrariaInstanceProvider
    {
        public InstanceType InstanceType { get; } = InstanceType.None;
        // start-server.bat
        public TargetType TargetType { get; } = TargetType.Script;
        public CreateTerrariaInstanceProvider()
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