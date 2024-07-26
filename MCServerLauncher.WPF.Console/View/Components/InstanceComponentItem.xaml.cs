namespace MCServerLauncher.WPF.Console.View.Components
{
    /// <summary>
    ///     InstanceComponentItem.xaml 的交互逻辑
    /// </summary>
    public partial class InstanceComponentItem
    {
        public InstanceComponentItem()
        {
            InitializeComponent();
        }
        public string ComponentFileName
        {
            get => ComponentFileNameTextBlock.Text;
            set => ComponentFileNameTextBlock.Text = value;
        }
    }
}
