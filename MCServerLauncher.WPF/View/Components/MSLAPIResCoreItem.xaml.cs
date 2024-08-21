namespace MCServerLauncher.WPF.View.Components
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

        /// <summary>
        /// Core name.
        /// </summary>
        public string CoreName
        {
            get => CoreNameReplacer.Text;
            set => CoreNameReplacer.Text = value;
        }

        /// <summary>
        /// Raw API core name.
        /// </summary>
        public string ApiActualName { get; set; }
    }
}