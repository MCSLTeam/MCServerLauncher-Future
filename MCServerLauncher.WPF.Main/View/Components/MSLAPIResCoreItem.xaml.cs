namespace MCServerLauncher.WPF.Main.View.Components
{
    /// <summary>
    ///     MSLAPIResCoreItem.xaml 的交互逻辑
    /// </summary>
    public partial class MSLAPIResCoreItem
    {
        public MSLAPIResCoreItem()
        {
            InitializeComponent();
        }

        public string CoreName
        {
            get => CoreNameReplacer.Text;
            set => CoreNameReplacer.Text = value;
        }

        public string ApiActualName { get; set; }
    }
}