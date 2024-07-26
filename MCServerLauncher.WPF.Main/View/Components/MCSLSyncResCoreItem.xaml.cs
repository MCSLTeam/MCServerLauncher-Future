namespace MCServerLauncher.WPF.Main.View.Components
{
    /// <summary>
    ///     MCSLSyncResCoreItem.xaml 的交互逻辑
    /// </summary>
    public partial class MCSLSyncResCoreItem
    {
        public MCSLSyncResCoreItem()
        {
            InitializeComponent();
        }

        public string CoreName
        {
            get => CoreNameReplacer.Text;
            set => CoreNameReplacer.Text = value;
        }
    }
}