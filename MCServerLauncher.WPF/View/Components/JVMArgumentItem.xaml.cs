using System.Windows;
using ListView = iNKORE.UI.WPF.Modern.Controls.ListView;

namespace MCServerLauncher.WPF.View.Components
{
    /// <summary>
    ///    JvmArgumentItem.xaml 的交互逻辑
    /// </summary>
    public partial class JvmArgumentItem
    {
        public JvmArgumentItem()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    JVM argument.
        /// </summary>
        public string Argument
        {
            get => ArgumentTextBox.Text;
            set => ArgumentTextBox.Text = value;
        }

        /// <summary>
        ///    Delete self.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DeleteArgument(object sender, RoutedEventArgs e)
        {
            ((ListView)Parent).Items.Remove(this);
        }
    }
}